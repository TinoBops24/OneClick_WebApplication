using Microsoft.AspNetCore.Mvc;
using OneClick_WebApp.Helpers;
using OneClick_WebApp.Models;
using OneClick_WebApp.Models.ViewModel;
using OneClick_WebApp.Services;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System;
using System.Text;
using System.Net;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Authorization;
using OneClick_WebApp.Models.Enums;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Filters;

namespace OneClick_WebApp.Pages.Cart
{
    public class IndexModel : BasePageModel
    {
        private const string CartSessionKey = "Cart";
        private readonly POSIntegrationService _posService;

        public List<CartItem> Cart { get; set; } = new();
        public List<Product> ProductsInCart { get; set; } = new();
        public double GrandTotal { get; set; }
        public double EstimatedDeliveryFee { get; set; } = 0;
        public double TotalWithDelivery => GrandTotal + EstimatedDeliveryFee;

        public string UserId { get; private set; }
        public string UserEmail { get; private set; }
        public string UserName { get; private set; }

        [BindProperty]
        public CheckoutInputModel Input { get; set; } = new CheckoutInputModel();

        
        public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            
            var isPostRequest = context.HttpContext.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase);

            if (isPostRequest)
            {
                
                await LoadSiteSettingsAsync();
                await next.Invoke();
            }
            else
            {
                
                await base.OnPageHandlerExecutionAsync(context, next);
            }
        }

        public class CheckoutInputModel : IValidatableObject
        {
            
            [Display(Name = "Delivery Type")]
            public DeliveryOption DeliveryType { get; set; } = DeliveryOption.Delivery;

           
            [Display(Name = "Your Contact Number")]
            [Required(ErrorMessage = "Your contact number is required.")]
            [RegularExpression(@"^(\+27|0)[0-9]{9}$|^(\+258|00258)[0-9]{8,9}$",
                ErrorMessage = "Please enter a valid South African (+27) or Mozambican (+258) phone number.")]
            public string OrdererPhone { get; set; }

            
            [Display(Name = "This order is for someone else")]
            public bool IsGiftOrder { get; set; } = false;

            [Display(Name = "Recipient Name")]
            public string RecipientName { get; set; }

            [Display(Name = "Recipient Phone (optional)")]
            [RegularExpression(@"^(\+27|0)[0-9]{9}$|^(\+258|00258)[0-9]{8,9}$|^$",
                ErrorMessage = "Please enter a valid phone number or leave blank.")]
            public string RecipientPhone { get; set; }

            
            [Display(Name = "Delivery Address")]
            [StringLength(300, MinimumLength = 0, ErrorMessage = "Address must be at most 300 characters.")]
            public string DeliveryAddress { get; set; }

            [Display(Name = "Delivery Instructions (optional)")]
            [StringLength(500, ErrorMessage = "Instructions cannot exceed 500 characters.")]
            public string DeliveryInstructions { get; set; }

            
            [Display(Name = "Gift Message (optional)")]
            [StringLength(250, ErrorMessage = "Gift message cannot exceed 250 characters.")]
            public string GiftMessage { get; set; }

           
            [Display(Name = "Preferred Contact Method")]
            public ContactMethod PreferredContact { get; set; } = ContactMethod.WhatsApp;

            
            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                // Gift orders require a recipient name
                if (IsGiftOrder && string.IsNullOrWhiteSpace(RecipientName))
                {
                    yield return new ValidationResult(
                        "Recipient name is required when ordering for someone else.",
                        new[] { nameof(RecipientName) });
                }

                // Delivery vs Pickup address handling
                if (DeliveryType == DeliveryOption.Delivery)
                {
                    // For Delivery, address must be present with sensible length
                    if (string.IsNullOrWhiteSpace(DeliveryAddress) || DeliveryAddress.Trim().Length < 10)
                    {
                        yield return new ValidationResult(
                            "Delivery address is required and must be at least 10 characters.",
                            new[] { nameof(DeliveryAddress) });
                    }
                }
                else // Pickup
                {
                    // Coerce to a sentinel so downstream code has a value
                    DeliveryAddress = "Pickup from store";
                }
            }
        }

        public enum DeliveryOption
        {
            [Display(Name = "Home Delivery")]
            Delivery,
            [Display(Name = "Store Pickup")]
            Pickup
        }

        public enum ContactMethod
        {
            [Display(Name = "WhatsApp")]
            WhatsApp,
            [Display(Name = "SMS")]
            SMS,
            [Display(Name = "Phone Call")]
            Phone
        }

        public IndexModel(FirebaseDbService dbService, POSIntegrationService posService) : base(dbService)
        {
            _posService = posService;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
        private string GetUserEmail() => User.FindFirstValue(ClaimTypes.Email);
        private string GetUserName() => User.Identity?.Name ?? GetUserEmail() ?? "Guest";

        public async Task OnGetAsync()
        {
            await LoadSiteSettingsAsync();
            UserId = GetUserId();
            UserEmail = GetUserEmail();
            UserName = GetUserName();

           
            Cart = HttpContext.Session.GetObject<List<CartItem>>(CartSessionKey) ?? new List<CartItem>();

            // If user is authenticated and cart is empty in session, try to load from Firestore
            if (!Cart.Any() && !string.IsNullOrEmpty(UserId))
            {
                try
                {
                    var firestoreCart = await _dbService.GetDocumentAsync<dynamic>("carts", UserId);
                    if (firestoreCart != null && firestoreCart.items != null)
                    {
                        // Parse Firestore cart items
                        var firestoreItems = new List<CartItem>();
                        foreach (var item in firestoreCart.items)
                        {
                            firestoreItems.Add(new CartItem
                            {
                                ProductId = item.ProductId?.ToString() ?? "",
                                Quantity = int.Parse(item.Quantity?.ToString() ?? "1")
                            });
                        }

                        if (firestoreItems.Any())
                        {
                            Cart = firestoreItems;
                            // Update session with Firestore data
                            HttpContext.Session.SetObject(CartSessionKey, Cart);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARNING] Failed to load cart from Firestore: {ex.Message}");
                    // Continue with empty cart
                }
            }

            if (Cart.Any())
            {
                var productIds = Cart.Select(c => c.ProductId).ToList();
                var productsQuery = _dbService.GetCollection("product").WhereIn(FieldPath.DocumentId, productIds);
                var productSnapshot = await productsQuery.GetSnapshotAsync();
                var productsDict = productSnapshot.Documents.ToDictionary(doc => doc.Id, doc => doc.ConvertTo<Product>());

                ProductsInCart = Cart
                    .Select(ci => productsDict.GetValueOrDefault(ci.ProductId))
                    .Where(p => p != null)
                    .ToList();

                GrandTotal = Cart.Sum(item => (productsDict.GetValueOrDefault(item.ProductId)?.Price ?? 0) * item.Quantity);
                EstimatedDeliveryFee = CalculateDeliveryFee(Input.DeliveryType, GrandTotal);
            }
        }
        private double CalculateDeliveryFee(DeliveryOption option, double subtotal)
        {
            // Free delivery on orders ≥ R500, otherwise R50. Pickup is always free
            if (option == DeliveryOption.Pickup) return 0;
            if (subtotal >= 500) return 0;
            return 50;
        }

        public async Task<JsonResult> OnGetCartWishlistCountsAsync()
        {
            var cart = HttpContext.Session.GetObject<List<CartItem>>(CartSessionKey) ?? new List<CartItem>();

            var wishlistCount = 0;
            if (User.Identity.IsAuthenticated)
            {
                var userId = GetUserId();
                var wishlistItems = await _dbService.GetWishlistItemsAsync(userId);
                wishlistCount = wishlistItems.Count;
            }

            return new JsonResult(new
            {
                cartCount = cart.Sum(x => x.Quantity),
                wishlistCount = wishlistCount
            });
        }

        public async Task<IActionResult> OnPostClearCartAsync()
        {
            HttpContext.Session.Remove(CartSessionKey);

            var userId = GetUserId();
            if (!string.IsNullOrEmpty(userId))
            {
                try
                {
                    await _dbService.DeleteDocumentAsync("carts", userId);
                }
                catch
                {
                    // Cart document might not exist, which is fine
                }
            }

            TempData["SuccessMessage"] = "Cart cleared successfully!";
            return RedirectToPage();
        }

        public async Task<JsonResult> OnPostRemoveFromCartAsync(string productId)
        {
            try
            {
                ModelState.Clear();
                Console.WriteLine($"[DEBUG] RemoveFromCart - ProductId: {productId}");

                if (string.IsNullOrEmpty(productId))
                {
                    return new JsonResult(new { success = false, message = "Product ID is required" });
                }

                // Get cart from session
                var cart = HttpContext.Session.GetObject<List<CartItem>>(CartSessionKey) ?? new List<CartItem>();
                Console.WriteLine($"[DEBUG] Current cart has {cart.Count} items");

                // Remove item
                var itemToRemove = cart.FirstOrDefault(x => x.ProductId == productId);
                if (itemToRemove == null)
                {
                    Console.WriteLine($"[ERROR] Item not found for removal: {productId}");
                    return new JsonResult(new { success = false, message = "Item not found in cart" });
                }

                cart.Remove(itemToRemove);
                Console.WriteLine($"[DEBUG] Item removed. Cart now has {cart.Count} items");

                // Update session
                HttpContext.Session.SetObject(CartSessionKey, cart);
                await HttpContext.Session.CommitAsync();
                Console.WriteLine($"[DEBUG] Session updated");

                // Background Firestore sync
                var userId = GetUserId();
                if (!string.IsNullOrEmpty(userId))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            if (cart.Any())
                            {
                                var cartData = new
                                {
                                    items = cart.Select(c => new
                                    {
                                        ProductId = c.ProductId,
                                        Quantity = c.Quantity
                                    }).ToList(),
                                    updatedAt = DateTime.UtcNow
                                };
                                await _dbService.SetDocumentAsync("carts", userId, cartData);
                            }
                            else
                            {
                                await _dbService.DeleteDocumentAsync("carts", userId);
                            }
                            Console.WriteLine($"[DEBUG] Background Firestore sync completed");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[WARNING] Background Firestore sync failed: {ex.Message}");
                        }
                    });
                }

                return new JsonResult(new { success = true, message = "Item removed from cart" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Exception in RemoveFromCart: {ex.Message}\n{ex.StackTrace}");
                return new JsonResult(new { success = false, message = "Error removing item from cart" });
            }
        }

        public async Task<JsonResult> OnPostUpdateQuantityAsync(string productId, int quantity)
        {
            try
            {
                // Clear any existing model state
                ModelState.Clear();

                Console.WriteLine($"[DEBUG] UpdateQuantity - ProductId: {productId}, Quantity: {quantity}");

                // Validate parameters
                if (string.IsNullOrEmpty(productId) || quantity < 1 || quantity > 99)
                {
                    Console.WriteLine($"[ERROR] Invalid parameters - ProductId: '{productId}', Quantity: {quantity}");
                    return new JsonResult(new { success = false, message = "Invalid parameters" });
                }

                // Get current cart from session ONLY
                var cart = HttpContext.Session.GetObject<List<CartItem>>(CartSessionKey) ?? new List<CartItem>();
                Console.WriteLine($"[DEBUG] Current cart has {cart.Count} items");

                // Find and update the item
                var existingItem = cart.FirstOrDefault(x => x.ProductId == productId);
                if (existingItem == null)
                {
                    Console.WriteLine($"[ERROR] Item not found in cart: {productId}");
                    return new JsonResult(new { success = false, message = "Item not found in cart" });
                }

                // Update quantity
                existingItem.Quantity = quantity;
                Console.WriteLine($"[DEBUG] Updated item quantity to {quantity}");

                // Save to session immediately
                HttpContext.Session.SetObject(CartSessionKey, cart);
                await HttpContext.Session.CommitAsync();
                Console.WriteLine($"[DEBUG] Session updated and committed");

                // Background Firestore sync 
                var userId = GetUserId();
                if (!string.IsNullOrEmpty(userId))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            //simplified object for Firestore
                            var cartData = new
                            {
                                items = cart.Select(c => new
                                {
                                    ProductId = c.ProductId,
                                    Quantity = c.Quantity
                                }).ToList(),
                                updatedAt = DateTime.UtcNow
                            };

                            await _dbService.SetDocumentAsync("carts", userId, cartData);
                            Console.WriteLine($"[DEBUG] Background Firestore sync completed");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[WARNING] Background Firestore sync failed: {ex.Message}");
                        }
                    });
                }

                return new JsonResult(new { success = true, message = "Quantity updated successfully" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Exception in UpdateQuantity: {ex.Message}\n{ex.StackTrace}");
                return new JsonResult(new { success = false, message = "Error updating quantity" });
            }
        }


        [Authorize]
        public async Task<IActionResult> OnPostCheckoutAsync()
        {
            try
            {
                
                if (!ModelState.IsValid)
                {
                    var errors = ModelState
                        .Where(x => x.Value.Errors.Count > 0)
                        .Select(x => new { Field = x.Key, Errors = x.Value.Errors.Select(e => e.ErrorMessage) });
                    Console.WriteLine($"Checkout ModelState errors: {System.Text.Json.JsonSerializer.Serialize(errors)}");

                    // Debug
                    Console.WriteLine($"IsGiftOrder received: {Input?.IsGiftOrder}");
                    Console.WriteLine($"RecipientName received: '{Input?.RecipientName}'");
                    Console.WriteLine($"GiftMessage received: '{Input?.GiftMessage}'");
                    Console.WriteLine($"RecipientPhone received: '{Input?.RecipientPhone}'");

                    // Debug form data
                    foreach (var formKey in Request.Form.Keys)
                    {
                        Console.WriteLine($"Form data - {formKey}: '{Request.Form[formKey]}'");
                    }
                }

               
                UserId = GetUserId();
                UserEmail = GetUserEmail();
                UserName = GetUserName();

                if (string.IsNullOrEmpty(UserId))
                {
                    return RedirectToPage("/Account/Login");
                }

                Cart = HttpContext.Session.GetObject<List<CartItem>>(CartSessionKey) ?? new List<CartItem>();

                if (!Cart.Any())
                {
                    TempData["ErrorMessage"] = "Your cart is empty.";
                    return RedirectToPage();
                }

                // Load products and calculate totals
                var productIds = Cart.Select(c => c.ProductId).ToList();
                var productsQuery = _dbService.GetCollection("product").WhereIn(FieldPath.DocumentId, productIds);
                var productSnapshot = await productsQuery.GetSnapshotAsync();
                var productsDict = productSnapshot.Documents.ToDictionary(doc => doc.Id, doc => doc.ConvertTo<Product>());

                ProductsInCart = Cart.Select(ci => productsDict.GetValueOrDefault(ci.ProductId)).Where(p => p != null).ToList();
                GrandTotal = Cart.Sum(item => (productsDict.GetValueOrDefault(item.ProductId)?.Price ?? 0) * item.Quantity);
                EstimatedDeliveryFee = CalculateDeliveryFee(Input?.DeliveryType ?? DeliveryOption.Delivery, GrandTotal);

                
                if (Input == null)
                {
                    ModelState.AddModelError(string.Empty, "Checkout information is required.");
                    await LoadSiteSettingsAsync();
                    return Page();
                }

                
                if (Input.DeliveryType == DeliveryOption.Pickup)
                {
                    Input.DeliveryAddress = "Pickup from store";
                }

                
                if (!Input.IsGiftOrder)
                {
                    // Remove validation errors for gift-related fields when not a gift order
                    ModelState.Remove("Input.RecipientName");
                    ModelState.Remove("Input.RecipientPhone");
                    ModelState.Remove("Input.GiftMessage");
                }

                // Clear validation errors for always-optional fields
                ModelState.Remove("Input.DeliveryInstructions");
                if (!string.IsNullOrEmpty(Input.RecipientPhone))
                {
                    ModelState.Remove("Input.RecipientPhone"); // Only validate if provided
                }
                if (!string.IsNullOrEmpty(Input.GiftMessage))
                {
                    ModelState.Remove("Input.GiftMessage"); // Only validate if provided
                }

                // Manual validation for gift orders
                if (Input.IsGiftOrder && string.IsNullOrWhiteSpace(Input.RecipientName))
                {
                    ModelState.AddModelError("Input.RecipientName", "Recipient name is required when ordering for someone else.");
                }

                // Validation for delivery address
                if (Input.DeliveryType == DeliveryOption.Delivery &&
                    (string.IsNullOrWhiteSpace(Input.DeliveryAddress) || Input.DeliveryAddress.Trim().Length < 10))
                {
                    ModelState.AddModelError("Input.DeliveryAddress", "Delivery address is required and must be at least 10 characters.");
                }

                if (!ModelState.IsValid)
                {
                    await LoadSiteSettingsAsync();
                    return Page();
                }

                //  Validate stock
                var stockIssues = await ValidateStockAvailability(Cart, productsDict);
                if (stockIssues.Any())
                {
                    ModelState.AddModelError(string.Empty, $"Stock issues: {string.Join("; ", stockIssues)}");
                    await LoadSiteSettingsAsync();
                    return Page();
                }

                // branch configuration before creating transaction
                var settings = await _dbService.GetBranchConfigurationAsync();
                var branchId = settings?.BranchId ?? "almeida";

                //  customer info
                var customerInfo = new CustomerInfo
                {
                    ID = UserEmail,
                    Name = UserName,
                    WebName = UserName,
                    PhoneNumber = Input.OrdererPhone
                };

                // create transaction with proper error handling
                var transaction = await _posService.CreateTransactionAsync(branchId, customerInfo);

                
                var cartEntries = new Dictionary<string, CartEntry>();
                var enhancedStockMovements = new List<StockMovement>();

                foreach (var cartItem in Cart)
                {
                    if (productsDict.TryGetValue(cartItem.ProductId, out var product))
                    {
                        // cart entry for POS service 
                        cartEntries[cartItem.ProductId] = new CartEntry
                        {
                            Product = product,
                            Quantity = cartItem.Quantity
                        };

                        // complete StockMovement with all POS-required fields
                        var stockMovement = new StockMovement
                        {
                            // Core product and transaction info
                            Product = product,
                            Quantity = cartItem.Quantity,
                            UnitPrice = product.Price,
                            LineTotal = product.Price * cartItem.Quantity,

                            // timestamp and customer info
                            Timestamp = DateTime.UtcNow,
                            ForWho = Input.IsGiftOrder ? Input.RecipientName : UserName,
                            SalesRep = "Web Order",

                            // POS-required status fields
                            Printed = false,
                            Paid = false,
                            Selected = false,
                            Production = false,

                            // Discount fields (no discounts for standard web orders)
                            DiscountPercentage = 0.0,
                            DiscountAmount = 0.0,
                            DiscountPrice = product.Price,

                            // Stock tracking fields
                            ExpectedStock = 0,
                            DifferenceInStock = 0,
                            CurrentStockCount = 0,

                            // Optional POS fields
                            PumpID = null,
                            PumpGun = null
                        };

                        // Calculate IVA properly based on product settings
                        if (product.IVA)
                        {
                            stockMovement.IVATotal = product.IVAAmount * cartItem.Quantity;
                            stockMovement.LineTotalWithoutIVA = product.PriceWithoutIVA * cartItem.Quantity;
                        }
                        else
                        {
                            stockMovement.IVATotal = 0;
                            stockMovement.LineTotalWithoutIVA = stockMovement.LineTotal;
                        }

                        // Populate fields for compatibility
                        stockMovement.PopulateFromProduct();

                        enhancedStockMovements.Add(stockMovement);
                    }
                }

                
                transaction.StockMovements = enhancedStockMovements;

                // Calculate transaction totals 
                transaction.GrandTotal = enhancedStockMovements.Sum(sm => sm.LineTotal);
                transaction.IVAAmount = enhancedStockMovements.Sum(sm => sm.IVATotal);
                transaction.AmountBeforeIVA = enhancedStockMovements.Sum(sm => sm.LineTotalWithoutIVA);
                transaction.TotalCost = transaction.GrandTotal; // For retail sales

                // Enhance with order details
                transaction = EnhanceTransactionWithOrderDetails(transaction, Input, UserName, UserEmail);

                // Update stock movements with specific order info
                _posService.UpdateStockMovementsWithOrderInfo(transaction, Input.RecipientName, Input.IsGiftOrder);

                // Save transaction with proper error handling
                string transactionId;
                try
                {
                    transactionId = await _posService.AddOnlineOrderAsync(branchId, transaction);

                    if (string.IsNullOrEmpty(transactionId))
                    {
                        throw new Exception("Transaction ID was not returned from save operation");
                    }
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError(string.Empty, $"Failed to save order: {ex.Message}");
                    await LoadSiteSettingsAsync();
                    return Page();
                }

                // Update stock items for inventory tracking
                await UpdateStockItemsAsync(enhancedStockMovements, branchId);

                // Build WhatsApp message
                var message = BuildOrderMessage(UserName, UserEmail, Input, Cart, ProductsInCart);
                var encodedMessage = WebUtility.UrlEncode(message);
                var businessWhatsAppNumber = settings?.Phone ?? "+263773024350";
                var whatsAppUrl = $"https://wa.me/{businessWhatsAppNumber}?text={encodedMessage}";

                // Clear cart (session + Firestore)
                HttpContext.Session.Remove(CartSessionKey);
                try
                {
                    await _dbService.DeleteDocumentAsync("carts", UserId);
                }
                catch
                {
                   
                }

                // Success feedback
                var posIntegrationStatus = await _posService.CheckBranchPOSIntegrationAsync(branchId);
                TempData["OrderProcessingMessage"] = posIntegrationStatus
                    ? "Order successfully submitted to both database and POS system!"
                    : "Order successfully submitted to database!";

                TempData["SuccessMessage"] = "Your order has been placed successfully!";

                // Redirect to success page
                return RedirectToPage("/Checkout/Success", new { orderId = transactionId, whatsAppUrl });
            }
            catch (Exception ex)
            {
                //Debug
                Console.WriteLine($"Checkout error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                ModelState.AddModelError(string.Empty, "An error occurred while processing your order. Please try again or contact support.");

              
                await LoadSiteSettingsAsync();

                
                UserId = GetUserId();
                UserEmail = GetUserEmail();
                UserName = GetUserName();
                Cart = HttpContext.Session.GetObject<List<CartItem>>(CartSessionKey) ?? new List<CartItem>();

                if (Cart.Any())
                {
                    var productIds = Cart.Select(c => c.ProductId).ToList();
                    var productsQuery = _dbService.GetCollection("product").WhereIn(FieldPath.DocumentId, productIds);
                    var productSnapshot = await productsQuery.GetSnapshotAsync();
                    var productsDict = productSnapshot.Documents.ToDictionary(doc => doc.Id, doc => doc.ConvertTo<Product>());
                    ProductsInCart = Cart.Select(ci => productsDict.GetValueOrDefault(ci.ProductId)).Where(p => p != null).ToList();
                    GrandTotal = Cart.Sum(item => (productsDict.GetValueOrDefault(item.ProductId)?.Price ?? 0) * item.Quantity);
                    EstimatedDeliveryFee = CalculateDeliveryFee(Input?.DeliveryType ?? DeliveryOption.Delivery, GrandTotal);
                }

                return Page();
            }
        }

        private async Task<List<string>> ValidateStockAvailability(List<CartItem> cart, Dictionary<string, Product> productsDict)
        {
            var stockIssues = new List<string>();

            foreach (var cartItem in cart)
            {
                if (!productsDict.ContainsKey(cartItem.ProductId))
                {
                    stockIssues.Add("A product in your cart was not found in the catalogue.");
                    continue;
                }

                var product = productsDict[cartItem.ProductId];
                if (product.StockQuantity > 0 && product.StockQuantity < cartItem.Quantity)
                {
                    stockIssues.Add($"{product.Name} - Available: {product.StockQuantity}, Requested: {cartItem.Quantity}");
                }
            }

            return stockIssues;
        }

        private POSTransaction EnhanceTransactionWithOrderDetails(POSTransaction transaction, CheckoutInputModel input, string userName, string userEmail)
        {
            // Core contact information
            transaction.Phone = input.OrdererPhone;
            transaction.Address = input.DeliveryAddress;
            transaction.DeliveryAddress = input.DeliveryAddress;
            transaction.ClientAddress = input.DeliveryAddress;

            // Delivery type
            transaction.DeliveryType = input.DeliveryType == DeliveryOption.Pickup
                ? Models.Enums.DeliveryType.Pickup
                : Models.Enums.DeliveryType.Standard;

            // Build instructions
            var instructions = new StringBuilder();
            instructions.AppendLine("Online order via e-commerce platform");

            if (input.IsGiftOrder)
            {
                instructions.AppendLine($"GIFT ORDER - Recipient: {input.RecipientName}");
                if (!string.IsNullOrWhiteSpace(input.RecipientPhone))
                {
                    instructions.AppendLine($"Recipient Phone: {input.RecipientPhone}");
                }
                instructions.AppendLine($"Ordered by: {userName} ({userEmail})");

                if (!string.IsNullOrWhiteSpace(input.GiftMessage))
                {
                    instructions.AppendLine($"Gift Message: {input.GiftMessage}");
                }
            }

            if (!string.IsNullOrWhiteSpace(input.DeliveryInstructions))
            {
                instructions.AppendLine($"Delivery Instructions: {input.DeliveryInstructions}");
            }

            instructions.AppendLine($"Preferred Contact: {input.PreferredContact}");

            transaction.Instructions = instructions.ToString();

            // Update client information if it's a gift order
            if (input.IsGiftOrder)
            {
                transaction.ClientName = input.RecipientName;
                transaction.ClientPhoneNumber = input.RecipientPhone ?? input.OrdererPhone;
                // Keep original orderer info in instructions
            }

            return transaction;
        }

        private string BuildOrderMessage(string ordererName, string ordererEmail, CheckoutInputModel input,
            List<CartItem> cart, List<Product> allProducts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Hello, I'd like to place an order:\n");

            // Customer details
            sb.AppendLine("CUSTOMER DETAILS:");
            sb.AppendLine($"• Name: {ordererName}");
            sb.AppendLine($"• Email: {ordererEmail}");
            sb.AppendLine($"• Phone: {input.OrdererPhone}");

            // Recipient details (if different)
            if (input.IsGiftOrder)
            {
                sb.AppendLine("\nRECIPIENT DETAILS (Gift Order):");
                sb.AppendLine($"•Name: {input.RecipientName}");
                if (!string.IsNullOrWhiteSpace(input.RecipientPhone))
                    sb.AppendLine($"• Phone: {input.RecipientPhone}");
                if (!string.IsNullOrWhiteSpace(input.GiftMessage))
                    sb.AppendLine($"• Gift Message: {input.GiftMessage}");
            }

            // Delivery information
            sb.AppendLine("\nDELIVERY INFORMATION:");
            sb.AppendLine($"• Type: {input.DeliveryType}");
            sb.AppendLine($"• Address: {input.DeliveryAddress}");
            if (!string.IsNullOrWhiteSpace(input.DeliveryInstructions))
                sb.AppendLine($"• Instructions: {input.DeliveryInstructions}");
            sb.AppendLine($"• Preferred Contact: {input.PreferredContact}");

            // Order items
            sb.AppendLine("\nORDER SUMMARY:");
            double subtotal = 0;
            foreach (var item in cart)
            {
                var product = allProducts.FirstOrDefault(p => p.DocumentId == item.ProductId);
                if (product != null)
                {
                    double lineSubtotal = product.Price * item.Quantity;
                    subtotal += lineSubtotal;
                    sb.AppendLine($"•{product.Name} × {item.Quantity} @ R{product.Price:0.00} = R{lineSubtotal:0.00}");
                }
            }

            sb.AppendLine($"\nSubtotal: R{subtotal:0.00}");

            // Dynamic pricing
            var fee = CalculateDeliveryFee(input.DeliveryType, subtotal);
            if (fee > 0)
            {
                sb.AppendLine($"Delivery: R{fee:0.00}");
                sb.AppendLine($"Total: R{(subtotal + fee):0.00}");
            }
            else
            {
                sb.AppendLine("Delivery: FREE");
                sb.AppendLine($"Total: R{subtotal:0.00}");
            }

            sb.AppendLine("\nPlease confirm availability and payment options. Thank you!");

            return sb.ToString();
        }
       

        
     
        
        private async Task UpdateStockItemsAsync(List<StockMovement> stockMovements, string branchId)
        {
            try
            {
                foreach (var stockMovement in stockMovements)
                {
                    var productId = stockMovement.Product.DocumentId;

                    // create stock item
                    StockItem stockItem;
                    try
                    {
                        stockItem = await _dbService.GetDocumentAsync<StockItem>("stockItems", productId);

                      
                        if (stockItem == null)
                        {
                            throw new Exception("Stock item not found");
                        }
                    }
                    catch
                    {
                        
                        stockItem = new StockItem(stockMovement.Product, stockMovement.Product.StockQuantity);
                        Console.WriteLine($"[INFO] Created new stock item for product {productId}");
                    }

                    
                    if (stockItem.Product == null)
                    {
                        stockItem.Product = stockMovement.Product;
                        stockItem.ProductId = productId;
                    }

                    
                    if (stockItem.In == null) stockItem.In = new List<StockMovement>();
                    if (stockItem.Out == null) stockItem.Out = new List<StockMovement>();
                    if (stockItem.StockCounts == null) stockItem.StockCounts = new List<StockMovement>();

                    
                    stockItem.AddOutgoingStock(stockMovement);

                    // Update the stock item in database
                    await _dbService.SetDocumentAsync("stockItems", productId, stockItem);
                    Console.WriteLine($"[INFO] Updated stock item for product {productId}, new quantity: {stockItem.Quantity}");

                    
                    var updatedProduct = stockMovement.Product;
                    var newStockQuantity = Math.Max(0, updatedProduct.StockQuantity - stockMovement.Quantity);
                    updatedProduct.StockQuantity = newStockQuantity;

                    await _dbService.SetDocumentAsync("product", productId, updatedProduct);
                    Console.WriteLine($"[INFO] Updated product stock quantity for {productId}: {newStockQuantity}");
                }

                Console.WriteLine($"[SUCCESS] Successfully updated stock levels for {stockMovements.Count} products");
            }
            catch (Exception ex)
            {
                // Log the errors
                Console.WriteLine($"[WARNING] Failed to update stock items: {ex.Message}");
                Console.WriteLine($"[WARNING] Stack trace: {ex.StackTrace}");

               
            }
        }

    }
}