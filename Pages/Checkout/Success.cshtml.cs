using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OneClick_WebApp.Models;
using OneClick_WebApp.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Google.Cloud.Firestore;
using System.Security.Claims;

namespace OneClick_WebApp.Pages.Checkout
{
    public class SuccessModel : BasePageModel
    {
        public SuccessModel(FirebaseDbService dbService) : base(dbService) { }

        [BindProperty(SupportsGet = true)]
        public string OrderId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string WhatsAppUrl { get; set; }

        // Order details for display
        public POSTransaction Order { get; set; }
        public List<Product> OrderProducts { get; set; } = new();
        public string CustomerEmail { get; set; }
        public string CustomerName { get; set; }
        public double Subtotal { get; set; }
        public double DeliveryFee { get; set; }
        public double Total { get; set; }

        public async Task OnGetAsync()
        {
            await LoadSiteSettingsAsync();

            // Get customer information from claims
            CustomerEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "Guest";
            CustomerName = User.FindFirstValue(ClaimTypes.GivenName) ??
                          User.FindFirstValue(ClaimTypes.Name) ??
                          CustomerEmail?.Split('@')[0] ??
                          "Guest";

            // Load order details if OrderId is provided
            if (!string.IsNullOrEmpty(OrderId))
            {
                await LoadOrderDetails();
            }
            else
            {
                // Set default values if no order ID
                Subtotal = 0;
                DeliveryFee = 0;
                Total = 0;
            }
        }

        private async Task LoadOrderDetails()
        {
            try
            {
                // Load the order from transactions collection
                var orderDoc = await _dbService.GetCollection("transactions").Document(OrderId).GetSnapshotAsync();

                if (orderDoc.Exists)
                {
                    Order = orderDoc.ConvertTo<POSTransaction>();

                    // Load product details for order items
                    if (Order.StockMovements != null && Order.StockMovements.Any())
                    {
                        var productIds = Order.StockMovements
                            .Where(sm => sm.Product != null && !string.IsNullOrEmpty(sm.Product.DocumentId))
                            .Select(sm => sm.Product.DocumentId)
                            .Distinct()
                            .ToList();

                        if (productIds.Any())
                        {
                            try
                            {
                                var productsQuery = _dbService.GetCollection("product")
                                    .WhereIn(FieldPath.DocumentId, productIds);
                                var productSnapshot = await productsQuery.GetSnapshotAsync();

                                OrderProducts = productSnapshot.Documents
                                    .Select(doc => doc.ConvertTo<Product>())
                                    .Where(p => p != null)
                                    .ToList();
                            }
                            catch (System.Exception ex)
                            {
                                System.Console.WriteLine($"Error loading order products: {ex.Message}");
                                // Continue without product details
                            }
                        }
                    }

                    // Calculate totals from order data
                    if (Order.StockMovements != null && Order.StockMovements.Any())
                    {
                        Subtotal = Order.StockMovements.Sum(sm => sm.LineTotal);
                    }
                    else
                    {
                        Subtotal = Order.GrandTotal;
                    }

                    // Calculate delivery fee based on order type and subtotal
                    DeliveryFee = CalculateDeliveryFee(Order.DeliveryType, Subtotal);

                    // Use the grand total from the order
                    Total = Order.GrandTotal;

                    // Update customer info from order if available
                    if (!string.IsNullOrEmpty(Order.ClientName) && Order.ClientName != CustomerName)
                    {
                        // This might be a gift order, keep the original customer name
                        // but we could add recipient info if needed
                    }
                }
                else
                {
                    System.Console.WriteLine($"Order not found: {OrderId}");
                    // Set default values
                    Subtotal = 0;
                    DeliveryFee = 0;
                    Total = 0;
                }
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"Error loading order details: {ex.Message}");
                System.Console.WriteLine($"Stack trace: {ex.StackTrace}");

                // Set default values on error
                Subtotal = 0;
                DeliveryFee = 0;
                Total = 0;

                // Continue without order details - basic success page will still work
            }
        }

        private double CalculateDeliveryFee(OneClick_WebApp.Models.Enums.DeliveryType deliveryType, double subtotal)
        {
            // Match the logic from your cart page
            if (deliveryType == OneClick_WebApp.Models.Enums.DeliveryType.Pickup)
                return 0;

            if (subtotal >= 500)
                return 0;

            return 50;
        }

        // Helper method to get user ID if needed for future enhancements
        private string GetUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                   User.FindFirstValue("user_id");
        }

        // Helper method to format currency for South African Rand
        public string FormatCurrency(double amount)
        {
            return amount.ToString("C", new System.Globalization.CultureInfo("en-ZA"));
        }

        // Property to check if order has delivery
        public bool IsDeliveryOrder => Order?.DeliveryType != OneClick_WebApp.Models.Enums.DeliveryType.Pickup;

        // Property to check if order is a gift
        public bool IsGiftOrder => Order != null &&
                                  !string.IsNullOrEmpty(Order.ClientName) &&
                                  Order.ClientName != CustomerName;

        // Property to get recipient name for gift orders
        public string RecipientName => IsGiftOrder ? Order.ClientName : CustomerName;
    }
}