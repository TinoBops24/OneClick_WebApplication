using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OneClick_WebApp.Models;
using OneClick_WebApp.Models.Enums;
using OneClick_WebApp.Pages;
using OneClick_WebApp.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace OneClick_WebApp.Pages.Admin
{
    [Authorize(Policy = "AdminOnly")]
    public class OrderDetailsModel : BasePageModel
    {
        private readonly ILogger<OrderDetailsModel> _logger;
        private readonly POSIntegrationService _posService;

        // Define South African timezone
        private static readonly TimeZoneInfo SouthAfricanTimeZone = TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time");

        public OrderDetailsModel(FirebaseDbService dbService, POSIntegrationService posService, ILogger<OrderDetailsModel> logger) : base(dbService)
        {
            _logger = logger;
            _posService = posService;
        }

        [BindProperty(SupportsGet = true)]
        public string OrderId { get; set; }

        public POSTransaction Order { get; set; }
        public UserAccount Customer { get; set; }

        // POS Integration Status Info
        public bool IsPosIntegrationEnabled { get; set; }
        public bool IsPosOrderSynced { get; set; }
        public string PosOrderStatus { get; set; }

        [TempData]
        public string SuccessMessage { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            await LoadSiteSettingsAsync();

            if (string.IsNullOrEmpty(OrderId))
            {
                return RedirectToPage("/Admin/Orders");
            }

            await LoadOrderDetailsAsync();

            if (Order == null)
            {
                ErrorMessage = "Order not found.";
                return RedirectToPage("/Admin/Orders");
            }

            // Load POS integration status
            await LoadPosIntegrationStatusAsync();

            return Page();
        }

        public async Task<IActionResult> OnPostUpdateStatusAsync(OnlineOrderStatus newStatus)
        {
            if (string.IsNullOrEmpty(OrderId))
            {
                return RedirectToPage("/Admin/Orders");
            }

            try
            {
                var order = await _dbService.GetOrderByIdAsync(OrderId);
                if (order != null)
                {
                    var oldStatus = order.OrderStatus;
                    order.OrderStatus = newStatus;

                    // Update fulfillment status based on order status
                    order.FulfillmentStatus = MapOrderStatusToFulfillment(newStatus);

                    await _dbService.SetDocumentAsync("transactions", order.Id, order);

                    SuccessMessage = $"Order status updated from {oldStatus} to {newStatus}";
                    _logger.LogInformation("Order {OrderId} status updated from {OldStatus} to {NewStatus}", OrderId, oldStatus, newStatus);

                    // Update POS system if integration is enabled
                    var posEnabled = await _posService.CheckBranchPOSIntegrationAsync(order.BranchDbName ?? "default");
                    if (posEnabled)
                    {
                        try
                        {
                            await _posService.SaveToPOSAsync(order.BranchDbName ?? "default", order);
                            _logger.LogInformation("Order status sync'd to POS system: {OrderId}", OrderId);
                        }
                        catch (Exception posEx)
                        {
                            _logger.LogWarning(posEx, "Failed to sync order status to POS system: {OrderId}", OrderId);
                            SuccessMessage += " (Note: POS sync failed - check POS integration settings)";
                        }
                    }
                }
                else
                {
                    ErrorMessage = "Order not found.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update order status for {OrderId}", OrderId);
                ErrorMessage = "Failed to update order status. Please try again.";
            }

            return RedirectToPage(new { OrderId });
        }

        public async Task<IActionResult> OnPostResyncToPosAsync()
        {
            if (string.IsNullOrEmpty(OrderId))
            {
                return RedirectToPage("/Admin/Orders");
            }

            try
            {
                var order = await _dbService.GetOrderByIdAsync(OrderId);
                if (order != null)
                {
                    var posEnabled = await _posService.CheckBranchPOSIntegrationAsync(order.BranchDbName ?? "default");
                    if (posEnabled)
                    {
                        await _posService.SaveToPOSAsync(order.BranchDbName ?? "default", order);
                        SuccessMessage = "Order successfully re-synced to POS system";
                        _logger.LogInformation("Order manually re-synced to POS: {OrderId}", OrderId);
                    }
                    else
                    {
                        ErrorMessage = "POS integration is disabled. Enable it in System Settings first.";
                    }
                }
                else
                {
                    ErrorMessage = "Order not found.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resync order to POS: {OrderId}", OrderId);
                ErrorMessage = "Failed to sync order to POS system. Please check your POS integration settings.";
            }

            return RedirectToPage(new { OrderId });
        }

        private async Task LoadOrderDetailsAsync()
        {
            try
            {
                Order = await _dbService.GetOrderByIdAsync(OrderId);
                if (Order != null && !string.IsNullOrEmpty(Order.ClientId))
                {
                    Customer = await _dbService.GetUserByIdAsync(Order.ClientId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load order details for {OrderId}", OrderId);
                Order = null;
            }
        }

        private async Task LoadPosIntegrationStatusAsync()
        {
            try
            {
                if (Order != null)
                {
                    IsPosIntegrationEnabled = await _posService.CheckBranchPOSIntegrationAsync(Order.BranchDbName ?? "default");

                    if (IsPosIntegrationEnabled)
                    {
                        // Check if this order exists in POS collection
                        var config = await _dbService.GetBranchConfigurationAsync();
                        var posCollectionName = config?.PosCollectionName ?? "POSOrders";

                        // Try to find the POS order (simplified check)
                        try
                        {
                            var posOrders = await _dbService.GetAllDocumentsAsync<object>(posCollectionName);
                            IsPosOrderSynced = posOrders.Any();
                            PosOrderStatus = IsPosOrderSynced ? "Synced" : "Not Found";
                        }
                        catch
                        {
                            IsPosOrderSynced = false;
                            PosOrderStatus = "Unknown";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check POS integration status for order {OrderId}", OrderId);
                IsPosIntegrationEnabled = false;
                IsPosOrderSynced = false;
            }
        }

        private FulfillmentStatus MapOrderStatusToFulfillment(OnlineOrderStatus orderStatus)
        {
            return orderStatus switch
            {
                OnlineOrderStatus.New => FulfillmentStatus.Pending,
                OnlineOrderStatus.Accepted => FulfillmentStatus.Processing,
                OnlineOrderStatus.Processing => FulfillmentStatus.Processing,
                OnlineOrderStatus.ReadyForCollection => FulfillmentStatus.ReadyForPickup,
                OnlineOrderStatus.Completed => FulfillmentStatus.Delivered,
                OnlineOrderStatus.Declined => FulfillmentStatus.Cancelled,
                OnlineOrderStatus.Cancelled => FulfillmentStatus.Cancelled,
                _ => FulfillmentStatus.Pending
            };
        }

        /// <summary>
        /// Helper method to convert UTC timestamp to South African time
        /// </summary>
        public string GetLocalTime(Google.Cloud.Firestore.Timestamp? timestamp)
        {
            if (timestamp == null) return "-";

            var utcDateTime = timestamp.Value.ToDateTime();
            var localDateTime = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, SouthAfricanTimeZone);
            return localDateTime.ToString("dd/MM/yyyy HH:mm");
        }

        /// <summary>
        /// Helper method to convert UTC timestamp to South African time (date only)
        /// </summary>
        public string GetLocalDate(Google.Cloud.Firestore.Timestamp? timestamp)
        {
            if (timestamp == null) return "-";

            var utcDateTime = timestamp.Value.ToDateTime();
            var localDateTime = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, SouthAfricanTimeZone);
            return localDateTime.ToString("dd/MM/yyyy");
        }

        /// <summary>
        /// Helper method to format delivery instructions with proper line breaks
        /// </summary>
        public string FormatInstructions(string instructions)
        {
            if (string.IsNullOrWhiteSpace(instructions))
                return string.Empty;

            // Split on sentence endings and rejoin with line breaks
            var sentences = System.Text.RegularExpressions.Regex.Split(instructions, @"(?<=[.!?])\s+");
            return string.Join("<br/>", sentences.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
    }
}