using Google.Cloud.Firestore;
using OneClick_WebApp.Models;
using OneClick_WebApp.Models.Enums;
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.Linq;
using System.Threading.Tasks;

namespace OneClick_WebApp.Services
{
    public class POSIntegrationService
    {
        private readonly FirebaseDbService _dbService;
        private readonly ILogger<POSIntegrationService> _logger;

        public POSIntegrationService(FirebaseDbService dbService, ILogger<POSIntegrationService> logger)
        {
            _dbService = dbService;
            _logger = logger;
        }

        /// <summary>
        /// Creates transaction structure
        /// </summary>
        public async Task<POSTransaction> CreateTransactionAsync(string branchId, CustomerInfo client)
        {
            var transaction = new POSTransaction
            {
                // TransactionId maps to "ID" in Firestore
                TransactionId = Guid.NewGuid().ToString(),
                Timestamp = Timestamp.GetCurrentTimestamp(),
                DueDate = null,
                ClientId = client.ID,
                ClientName = client.Name,
                ClientWebName = client.WebName,
                ClientPhoneNumber = client.PhoneNumber,
                ClientNuit = null,
                ClientAddress = null,
                StockMovements = new List<StockMovement>(),
                MovementType = "out", 
                GrandTotal = 0.0,
                Read = false,
                OrderStatus = OnlineOrderStatus.New, 
                BankIdPaidTo = null,
                AmountPaid = 0.0,
                Change = 0.0,
                Payments = new List<Payment>(),
                ReceiptPrinted = false, 
                InvoicePrinted = false,  
                Instructions = "",
                CompletionTime = null,
                RemindInMinutes = 0, 
                PickupLocation = "", 
                BranchDbName = branchId,
                Reversed = false,
                ProofOfPaymentAttachment = "",
                IVAAmount = 0.0,
                AmountBeforeIVA = 0.0,
                Packaged = false, 
                OfficeClosingBalance = 0.0,
                OfficeOpeningBalance = 0.0,
                ClientClosingBalance = 0.0,
                ClientOpeningBalance = 0.0,
                SupplierClosingBalance = 0.0,
                SupplierOpeningBalance = 0.0,
                BankClosingBalance = 0.0,
                BankOpeningBalance = 0.0,
                StaffResponsible = null,
                SalesRep = "",
                FaturaComment = null,
                AdminComments = new List<Models.Comment>(),
                DiscountAmount = 0.0,
                AmountPending = 0.0,
                PartialPayment = PartialType.None,
                Type = TransactionType.Sale,
                Online = true,
                DeliveryType = DeliveryType.Standard
            };

            return transaction;
        }

        /// <summary>
        ///Creates complete StockMovements with all POS-required fields
        /// </summary>
        public POSTransaction AddStockMovementsToTransaction(Dictionary<string, CartEntry> cart, POSTransaction transaction)
        {
            var stockMovements = new List<StockMovement>();
            double totalIva = 0.0;
            double grandTotal = 0.0;

            foreach (var entry in cart.Values)
            {
                var product = entry.Product;
                double quantity = (double)entry.Quantity; 
                double price = product.Price;

              
                try
                {
                    if (product.Grams)
                    {
                        quantity = quantity * 1000;
                    }
                }
                catch
                {
                    
                }

                double lineTotal = price * quantity;
                double ivaTotal = 0.0;
                double lineTotalWithoutIva = lineTotal;

                // Calculate IVA if applicable 
                if (product.IVA)
                {
                    double ivaPercentage = product.IVAPercentage;
                    double priceWithoutIva = product.PriceWithoutIVA;
                    double ivaAmount = product.IVAAmount;

                    ivaTotal = ivaAmount * quantity;
                    lineTotalWithoutIva = priceWithoutIva * quantity;
                }

                
                var stockMovement = new StockMovement
                {
                    // Core product and transaction info
                    Timestamp = DateTime.Now, // Use DateTime instead of Timestamp
                    Product = product, 
                    Quantity = (int)quantity,
                    UnitPrice = price,
                    LineTotal = lineTotal,
                    IVATotal = ivaTotal,
                    LineTotalWithoutIVA = lineTotalWithoutIva,
                    DiscountPercentage = 0.0, //

                    
                    ForWho = "Customer", 
                    SalesRep = "Web Order",
                    Printed = false,
                    Paid = false,
                    Selected = false,
                    Production = false,

                    // Discount fields
                    DiscountAmount = 0.0,
                    DiscountPrice = price,

                    // Stock tracking fields
                    ExpectedStock = 0,
                    DifferenceInStock = 0,
                    CurrentStockCount = 0,

                    // Optional fields
                    PumpID = null,
                    PumpGun = null
                };

                
                stockMovement.PopulateFromProduct();

                stockMovements.Add(stockMovement);
                totalIva += ivaTotal;
                grandTotal += lineTotal;
            }

            // Update the transaction 
            transaction.StockMovements = stockMovements;
            transaction.GrandTotal = grandTotal;
            transaction.IVAAmount = totalIva;
            transaction.AmountBeforeIVA = grandTotal - totalIva;
            transaction.TotalCost = grandTotal; // For retail sales

            return transaction;
        }

        /// <summary>
        /// Updates stock movements with specific customer and order info
        /// </summary>
        /// <summary>
        /// Updates stock movements with specific customer and order information
        /// Call this after transaction creation but before saving to POS
        /// </summary>
        public void UpdateStockMovementsWithOrderInfo(POSTransaction transaction, string recipientName = null, bool isGiftOrder = false)
        {
            if (transaction?.StockMovements == null)
            {
                Console.WriteLine("[WARNING] No stock movements found in transaction");
                return;
            }

            // Determine who the order is for
            var forWho = isGiftOrder && !string.IsNullOrEmpty(recipientName)
                ? recipientName
                : transaction.ClientName ?? "Customer";

            Console.WriteLine($"[INFO] Updating {transaction.StockMovements.Count} stock movements - ForWho: {forWho}");

            // Update each stock movement with correct customer info
            foreach (var stockMovement in transaction.StockMovements)
            {
                stockMovement.ForWho = forWho;
                stockMovement.SalesRep = "Web Order";

                // Ensure timestamp is set
                if (stockMovement.Timestamp == null)
                {
                    stockMovement.Timestamp = DateTime.UtcNow;
                }

                // Ensure legacy fields are populated
                if (string.IsNullOrEmpty(stockMovement.ProductId) && stockMovement.Product != null)
                {
                    stockMovement.PopulateFromProduct();
                }
            }

            Console.WriteLine($"[SUCCESS] Updated stock movements with order info");
        }

        /// <summary>
        /// Checks if POS integration is enabled for the branch
        /// </summary>
        public async Task<bool> CheckBranchPOSIntegrationAsync(string branchId)
        {
            try
            {
                var config = await _dbService.GetBranchConfigurationAsync();

                
                
                if (config?.BranchOnlineSaleToSystem != null &&
                    config.BranchOnlineSaleToSystem.ContainsKey(branchId))
                {
                    return config.BranchOnlineSaleToSystem[branchId];
                }

                // Fallback to simple toggle
                return config?.PosIntegrationEnabled ?? false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check POS integration status for branch {BranchId}", branchId);
                return false;
            }
        }

        /// <summary>
        /// Adds online order exactly matching Python db.add_online_order
        /// </summary>
        public async Task<string> AddOnlineOrderAsync(string branchId, POSTransaction transaction)
        {
            try
            {
                // 1. Always save to regular transactions collection
                var transactionId = await _dbService.SaveOrderAsync(transaction);
                _logger.LogInformation("Transaction saved to regular collection: {TransactionId}", transactionId);

                // 2. Check if POS integration is enabled
                var posIntegrationEnabled = await CheckBranchPOSIntegrationAsync(branchId);

                if (posIntegrationEnabled)
                {
                    // 3. Save to POS collection using Python path structure
                    await SaveToPOSAsync(branchId, transaction);
                    _logger.LogInformation("Transaction sent to POS system: {TransactionId}", transactionId);
                }
                else
                {
                    _logger.LogInformation("POS integration disabled - transaction saved to database only: {TransactionId}", transactionId);
                }

                return transactionId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add online order for branch {BranchId}", branchId);
                throw;
            }
        }

        /// <summary>
        /// Save using exact collection path structure
        /// </summary>
        public async Task SaveToPOSAsync(string branchId, POSTransaction transaction)
        {
            try
            {
                // exact POS compatible path
                var collectionPath = $"onlinesale/{branchId}/transaction";
                var documentId = transaction.TransactionId; // Use exact transaction ID

                // Convert to compatible structure
                var posTransaction = CreatePythonCompatibleTransaction(transaction);

                // Save using compatible collection structure
                await _dbService.SetDocumentAsync(collectionPath, documentId, posTransaction);

                _logger.LogInformation("Transaction saved to POS collection {Collection} with ID {DocumentId}",
                    collectionPath, documentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save transaction to POS system");
                throw;
            }
        }

        /// <summary>
        /// transaction structure that exactly matches Python output
        /// </summary>
        private object CreatePythonCompatibleTransaction(POSTransaction transaction)
        {
            
            return new Dictionary<string, object>
            {
                ["ID"] = transaction.TransactionId,
                ["Timestamp"] = transaction.Timestamp,
                ["DueDate"] = transaction.DueDate,
                ["clientID"] = transaction.ClientId,
                ["clientName"] = transaction.ClientName,
                ["clientWebName"] = transaction.ClientWebName,
                ["clientPhoneNumber"] = transaction.ClientPhoneNumber,
                ["clientNuit"] = transaction.ClientNuit,
                ["clientAddress"] = transaction.ClientAddress,
                ["stockMovements"] = transaction.StockMovements?.Select(sm => new Dictionary<string, object>
                {
                    ["Timestamp"] = sm.Timestamp,
                    ["LineTotal"] = sm.LineTotal,
                    ["product"] = sm.Product != null ? ConvertProductToDict(sm.Product) : new Dictionary<string, object>(),
                    ["Quantity"] = sm.Quantity,
                    ["DiscountPercentage"] = sm.DiscountPercentage,
                    ["IVATotal"] = sm.IVATotal,
                    ["LineTotalWithoutIVA"] = sm.LineTotalWithoutIVA,
                    // additional POS fields
                    ["ForWho"] = sm.ForWho,
                    ["SalesRep"] = sm.SalesRep,
                    ["Printed"] = sm.Printed,
                    ["Paid"] = sm.Paid,
                    ["Selected"] = sm.Selected,
                    ["Production"] = sm.Production,
                    ["DiscountAmount"] = sm.DiscountAmount,
                    ["DiscountPrice"] = sm.DiscountPrice
                }).ToArray() ?? new object[0],
                ["MovementType"] = transaction.MovementType, 
                ["GrandTotal"] = transaction.GrandTotal,
                ["Read"] = transaction.Read,
                ["OrderStatus"] = (int)transaction.OrderStatus, 
                ["bankIDPaidTo"] = transaction.BankIdPaidTo,
                ["AmountPaid"] = transaction.AmountPaid,
                ["Change"] = transaction.Change,
                ["Payments"] = transaction.Payments ?? new List<Payment>(),
                ["prePrinted"] = transaction.ReceiptPrinted, 
                ["kitchenPrinted"] = transaction.InvoicePrinted, 
                ["Instructions"] = transaction.Instructions ?? "",
                ["CompletionTime"] = transaction.CompletionTime,
                ["RemindInMinutes"] = transaction.RemindInMinutes, 
                ["ReservedFor"] = transaction.PickupLocation ?? "", 
                ["branchDBName"] = transaction.BranchDbName,
                ["reversed"] = transaction.Reversed,
                ["ProofofPaymentAttachment"] = transaction.ProofOfPaymentAttachment ?? "",
                ["IVAAmount"] = transaction.IVAAmount,
                ["AmountBeforeIVA"] = transaction.AmountBeforeIVA,
                ["Served"] = transaction.Packaged, 
                ["OfficeClosingBalance"] = transaction.OfficeClosingBalance,
                ["OfficeOpeningBalance"] = transaction.OfficeOpeningBalance,
                ["ClientClosingBalance"] = transaction.ClientClosingBalance,
                ["ClientOpeningBalance"] = transaction.ClientOpeningBalance,
                ["SupplierClosingBalance"] = transaction.SupplierClosingBalance,
                ["SupplierOpeningBalance"] = transaction.SupplierOpeningBalance,
                ["BankClosingBalance"] = transaction.BankClosingBalance,
                ["BankOpeningBalance"] = transaction.BankOpeningBalance,
                ["StaffResponsible"] = transaction.StaffResponsible,
                ["SalesRep"] = transaction.SalesRep ?? "",
                ["FaturaComment"] = transaction.FaturaComment,
                ["AdminComments"] = transaction.AdminComments ?? new List<Models.Comment>(),
                ["DiscountAmount"] = transaction.DiscountAmount,
                ["AmountPending"] = transaction.AmountPending,
                ["PartialPayment"] = (int)transaction.PartialPayment, 
                ["Type"] = (int)transaction.Type, 
                ["Online"] = transaction.Online,
                ["orderType"] = (int)transaction.DeliveryType 
            };
        }

        /// <summary>
        /// Convert Product object to Dictionary matching Python structure
        /// </summary>
        private Dictionary<string, object> ConvertProductToDict(Product product)
        {
            // product fields in the product class
            var productDict = new Dictionary<string, object>
            {
                ["ID"] = product.Id ?? product.DocumentId ?? "",
                ["DocumentId"] = product.DocumentId ?? "",
                ["Name"] = product.Name ?? "",
                ["Price"] = product.Price,
                ["PriceWithoutIVA"] = product.PriceWithoutIVA,
                ["IVAAmount"] = product.IVAAmount,
                ["IVAPercentage"] = product.IVAPercentage,
                ["IVA"] = product.IVA,
                ["SKU"] = product.SKU ?? "",
                ["Barcode"] = product.Barcode ?? "",
                ["Description"] = product.Description ?? "",
                ["StockQuantity"] = product.StockQuantity,
                ["Grams"] = product.Grams,
                ["CostPrice"] = product.CostPrice,
                ["NacalaPrice"] = product.NacalaPrice,
                ["LotNumber"] = product.LotNumber ?? "",
                ["ExpiryDate"] = product.ExpiryDate,
                ["Order"] = product.Order,
                ["CommentForProduct"] = product.CommentForProduct ?? "",
                ["HideInPOS"] = product.HideInPOS,
                ["HideInWeb"] = product.HideInWeb,
                ["PictureAttachment"] = product.ImageUrl ?? "",
                ["PreviousCostPrice"] = product.PreviousCostPrice,
                ["ProduceOnSale"] = product.ProduceOnSale,
                ["ProductType"] = product.ProductType
            };

            // Handle complex Category
            if (product.Category != null)
            {
                productDict["category"] = new Dictionary<string, object>
                {
                    ["ID"] = product.Category.Id ?? "",
                    ["Name"] = product.Category.Name ?? ""
                };
            }
            else
            {
                productDict["category"] = null;
            }

            // Handle complex types - Supplier  
            if (product.Supplier != null)
            {
                productDict["supplier"] = new Dictionary<string, object>
                {
                    ["ID"] = product.Supplier.ID ?? "",
                    ["Name"] = product.Supplier.Name ?? "",
                    ["AccountNo"] = product.Supplier.AccountNo ?? "",
                    ["BankName"] = product.Supplier.BankName ?? "",
                    ["Email"] = product.Supplier.Email ?? "",
                    ["PhoneNumber"] = product.Supplier.PhoneNumber ?? "",
                    ["ClosingBalance"] = product.Supplier.ClosingBalance,
                    ["CreditDays"] = product.Supplier.CreditDays,
                    ["Deleted"] = product.Supplier.Deleted,
                    ["HideFromAdmin"] = product.Supplier.HideFromAdmin
                };
            }
            else
            {
                productDict["supplier"] = null;
            }

            return productDict;
        }

        /// <summary>
        /// Add resto data equivalent (if needed for your use case)
        /// </summary>
        public POSTransaction AddRestoData(POSTransaction transaction, int orderType, DateTime? completionDateTime)
        {
            
            transaction.DeliveryType = (DeliveryType)orderType;

            
            if (completionDateTime.HasValue)
            {
                transaction.CompletionTime = Timestamp.FromDateTime(completionDateTime.Value);
                transaction.RemindInMinutes = 30; 
            }

            return transaction;
        }

        /// <summary>
        /// Validates stock levels before processing order
        /// </summary>
        public async Task<bool> ValidateStockAsync(Dictionary<string, CartEntry> cart)
        {
            try
            {
                var config = await _dbService.GetBranchConfigurationAsync();
                if (!config?.EnableStockValidation ?? false)
                {
                    return true; // Skip validation if disabled
                }

                foreach (var entry in cart.Values)
                {
                    var product = entry.Product;
                    if (product.StockQuantity < entry.Quantity)
                    {
                        _logger.LogWarning("Insufficient stock for product {ProductId}: Available {Available}, Requested {Requested}",
                            product.DocumentId, product.StockQuantity, entry.Quantity);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating stock levels");
                return false;
            }
        }
        
    }

    // SUPPORTING CLASSES
    public class CustomerInfo
    {
        public string ID { get; set; }
        public string Name { get; set; }
        public string WebName { get; set; }
        public string PhoneNumber { get; set; }
    }

   
}