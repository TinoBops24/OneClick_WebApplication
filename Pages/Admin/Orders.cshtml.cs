using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OneClick_WebApp.Models;
using OneClick_WebApp.Models.Enums;
using OneClick_WebApp.Pages;
using OneClick_WebApp.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OneClick_WebApp.Pages.Admin
{
    [Authorize(Policy = "AdminOnly")]
    public class OrdersModel : BasePageModel
    {
        private readonly ILogger<OrdersModel> _logger;
        private static readonly TimeZoneInfo SouthAfricanTimeZone = TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time");

        public OrdersModel(FirebaseDbService dbService, ILogger<OrdersModel> logger) : base(dbService)
        {
            _logger = logger;
        }

        public List<POSTransaction> Orders { get; set; } = new();
        public int TotalCount { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

        // Filter parameters
        [BindProperty(SupportsGet = true)]
        public string SearchTerm { get; set; }

        [BindProperty(SupportsGet = true)]
        public OnlineOrderStatus? StatusFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? DateFrom { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? DateTo { get; set; }

        [BindProperty(SupportsGet = true)]
        public int Page { get; set; } = 1;

        public async Task OnGetAsync()
        {
            await LoadSiteSettingsAsync();
            CurrentPage = Page;
            await LoadOrdersAsync();
        }

        /// <summary>
        /// Loads orders directly from database and applies filters in-memory.
        /// NO CACHING - Real-time data for admin
        /// </summary>
        private async Task LoadOrdersAsync()
        {
            try
            {
                // Load orders directly from database - NO CACHING
                var allOrders = await _dbService.GetAllOrdersAsync();

                _logger.LogInformation("Total orders in database: {TotalOrders}", allOrders.Count);

                // Apply filters 
                var filteredOrders = allOrders.AsEnumerable();

                // Search term filter (if provided)
                if (!string.IsNullOrWhiteSpace(SearchTerm))
                {
                    filteredOrders = filteredOrders.Where(o =>
                        (o.TransactionId?.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (o.ClientName?.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (o.ClientId?.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ?? false));

                    _logger.LogInformation("After search filter: {Count} orders", filteredOrders.Count());
                }

                
                if (DateFrom.HasValue || DateTo.HasValue)
                {
                    filteredOrders = filteredOrders.Where(o =>
                    {
                        if (o.Timestamp == null) return false;

                        // Convert Firebase UTC timestamp to SAST date
                        var orderDateUtc = o.Timestamp.Value.ToDateTime();
                        var orderDateSAST = TimeZoneInfo.ConvertTimeFromUtc(orderDateUtc, SouthAfricanTimeZone).Date;

                        bool passesDateFrom = true;
                        bool passesDateTo = true;

                        if (DateFrom.HasValue)
                        {
                            // Ensure incoming date parameter is treated as SAST midnight
                            var filterDateFrom = DateTime.SpecifyKind(DateFrom.Value.Date, DateTimeKind.Unspecified);
                            passesDateFrom = orderDateSAST >= filterDateFrom;
                        }

                        if (DateTo.HasValue)
                        {
                            // Ensure incoming date parameter is treated as SAST end-of-day (inclusive)
                            var filterDateTo = DateTime.SpecifyKind(DateTo.Value.Date, DateTimeKind.Unspecified);
                            passesDateTo = orderDateSAST <= filterDateTo;
                        }

                        return passesDateFrom && passesDateTo;
                    });

                    _logger.LogInformation("After date filters (From: {DateFrom}, To: {DateTo}): {Count} orders",
                        DateFrom, DateTo, filteredOrders.Count());
                }

                // Apply STATUS filter AFTER date filter
                if (StatusFilter.HasValue)
                {
                    if (StatusFilter.Value == OnlineOrderStatus.Processing)
                    {
                        // Match Dashboard: "Processing" means Processing OR Accepted
                        filteredOrders = filteredOrders.Where(o =>
                            o.OrderStatus == OnlineOrderStatus.Processing ||
                            o.OrderStatus == OnlineOrderStatus.Accepted);
                        _logger.LogInformation("After status filter (Processing OR Accepted): {Count} orders", filteredOrders.Count());
                    }
                    else if (StatusFilter.Value == OnlineOrderStatus.Completed)
                    {
                        // Match Dashboard: "Completed" means Completed OR ReadyForCollection
                        filteredOrders = filteredOrders.Where(o =>
                            o.OrderStatus == OnlineOrderStatus.Completed ||
                            o.OrderStatus == OnlineOrderStatus.ReadyForCollection);
                        _logger.LogInformation("After status filter (Completed OR ReadyForCollection): {Count} orders", filteredOrders.Count());
                    }
                    else
                    {
                        // For other statuses, filter normally
                        filteredOrders = filteredOrders.Where(o => o.OrderStatus == StatusFilter.Value);
                        _logger.LogInformation("After status filter ({Status}): {Count} orders", StatusFilter.Value, filteredOrders.Count());
                    }
                }

                TotalCount = filteredOrders.Count();

                _logger.LogInformation("Final filtered count: {Count} orders", TotalCount);

                // Apply pagination
                Orders = filteredOrders
                    .OrderByDescending(o => o.Timestamp)
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .ToList();

                _logger.LogInformation("Loaded {OrderCount} orders (page {CurrentPage}/{TotalPages})",
                    Orders.Count, CurrentPage, TotalPages);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Orders load failed");
                Orders = new List<POSTransaction>();
                TotalCount = 0;
            }
        }

        /// <summary>
        /// Updates order status - NO cache invalidation needed anymore.
        /// </summary>
        public async Task<IActionResult> OnPostUpdateStatusAsync(string orderId, OnlineOrderStatus newStatus)
        {
            try
            {
                var order = await _dbService.GetOrderByIdAsync(orderId);
                if (order != null)
                {
                    order.OrderStatus = newStatus;
                    await _dbService.SetDocumentAsync("transactions", order.Id, order);

                    _logger.LogInformation("Order {OrderId} status updated to {NewStatus}", orderId, newStatus);

                    // Set appropriate message and type based on status
                    var message = $"Order {orderId} status updated to {newStatus}";

                    switch (newStatus)
                    {
                        case OnlineOrderStatus.Completed:
                            TempData["SuccessMessage"] = message;
                            break;
                        case OnlineOrderStatus.Cancelled:
                        case OnlineOrderStatus.Declined:
                            TempData["WarningMessage"] = message;
                            break;
                        case OnlineOrderStatus.Processing:
                            TempData["InfoMessage"] = message;
                            break;
                        default:
                            TempData["InfoMessage"] = message;
                            break;
                    }
                }
                else
                {
                    TempData["ErrorMessage"] = $"Order {orderId} not found";
                }

                // Build query string to preserve filters
                var queryParams = new List<string>();

                if (Page > 1)
                    queryParams.Add($"Page={Page}");

                if (!string.IsNullOrWhiteSpace(SearchTerm))
                    queryParams.Add($"SearchTerm={Uri.EscapeDataString(SearchTerm)}");

                if (StatusFilter.HasValue)
                    queryParams.Add($"StatusFilter={StatusFilter.Value}");

                if (DateFrom.HasValue)
                    queryParams.Add($"DateFrom={DateFrom.Value:yyyy-MM-dd}");

                if (DateTo.HasValue)
                    queryParams.Add($"DateTo={DateTo.Value:yyyy-MM-dd}");

                var redirectUrl = queryParams.Any()
                    ? $"/Admin/Orders?{string.Join("&", queryParams)}"
                    : "/Admin/Orders";

                return Redirect(redirectUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order status update failed: {OrderId}", orderId);
                TempData["ErrorMessage"] = "Failed to update order status. Please try again.";
                return Redirect("/Admin/Orders");
            }
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
    }
}