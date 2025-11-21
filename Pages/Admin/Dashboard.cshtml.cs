using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OneClick_WebApp.Models;
using OneClick_WebApp.Models.Enums;
using OneClick_WebApp.Pages;
using OneClick_WebApp.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace OneClick_WebApp.Pages.Admin
{
    [Authorize(Policy = "AdminOnly")]
    public class DashboardModel : BasePageModel
    {
        private readonly ILogger<DashboardModel> _logger;

        // Cross-platform SA timezone (Windows + Linux)
        private static readonly TimeZoneInfo SouthAfricanTimeZone = ResolveSouthAfricaTimeZone();

        public DashboardModel(FirebaseDbService dbService, ILogger<DashboardModel> logger) : base(dbService)
        {
            _logger = logger;
        }

        // Date range (UI values: Today, Yesterday, Last7Days, Last30Days, AllTime)
        [BindProperty(SupportsGet = true)]
        public string DateRange { get; set; } = "Last30Days";

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string DateRangeDisplay { get; set; }

        // Date-range metrics
        public double TotalRevenue { get; set; }
        public int TotalOrders { get; set; }
        public int OrdersThisMonth { get; set; }
        public int OrdersToday { get; set; }
        public int OrdersThisWeek { get; set; }
        public int NewOrdersCount { get; set; }
        public int ProcessingOrdersCount { get; set; }
        public int CompletedOrdersCount { get; set; }

        // Lifetime customer insights
        public int NewBuyersCount { get; set; }
        public int FrequentBuyersCount { get; set; }
        public double NewBuyersPercentage { get; set; }
        public double FrequentBuyersPercentage { get; set; }
        public List<CustomerSegmentData> NewBuyersList { get; set; } = new();
        public List<CustomerSegmentData> FrequentBuyersList { get; set; } = new();

        // Lists
        public List<POSTransaction> RecentOrders { get; set; } = new();
        public List<DailyOrderStats> OrdersChartData { get; set; } = new();

        public async Task OnGetAsync()
        {
            await LoadSiteSettingsAsync();

            // 1) date range
            CalculateDateRange();

            // 2) date-range metrics/charts
            await LoadDashboardAnalyticsAsync();

            // 3) lifetime customer insights
            await LoadCustomerSegmentationLifetimeAsync();
        }

        private static TimeZoneInfo ResolveSouthAfricaTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time"); }
            catch
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"); }
                catch { return TimeZoneInfo.Utc; }
            }
        }

        private void CalculateDateRange()
        {
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SouthAfricanTimeZone);
            var today = localNow.Date;

            switch (DateRange)
            {
                case "Today":
                    StartDate = today;
                    EndDate = today.AddDays(1).AddSeconds(-1);
                    DateRangeDisplay = $"Today ({today:dd MMM yyyy})";
                    break;

                case "Yesterday":
                    StartDate = today.AddDays(-1);
                    EndDate = today.AddSeconds(-1);
                    DateRangeDisplay = $"Yesterday ({StartDate:dd MMM yyyy})";
                    break;

                case "Last7Days":
                    StartDate = today.AddDays(-6);
                    EndDate = today.AddDays(1).AddSeconds(-1);
                    DateRangeDisplay = $"Last 7 Days ({StartDate:dd MMM yyyy} - {today:dd MMM yyyy})";
                    break;

                case "Last30Days":
                    StartDate = today.AddDays(-29);
                    EndDate = today.AddDays(1).AddSeconds(-1);
                    DateRangeDisplay = $"Last 30 Days ({StartDate:dd MMM yyyy} - {today:dd MMM yyyy})";
                    break;

                case "AllTime":
                    StartDate = DateTime.MinValue;
                    EndDate = DateTime.MaxValue;
                    DateRangeDisplay = "All Time";
                    break;

                default:
                    StartDate = today.AddDays(-29);
                    EndDate = today.AddDays(1).AddSeconds(-1);
                    DateRangeDisplay = $"Last 30 Days ({StartDate:dd MMM yyyy} - {today:dd MMM yyyy})";
                    break;
            }
        }

        private async Task LoadDashboardAnalyticsAsync()
        {
            try
            {
                var allOrders = await _dbService.GetAllOrdersAsync();

                var filteredOrders = allOrders.Where(o =>
                {
                    if (o.Timestamp == null) return false;
                    var orderDateLocal = TimeZoneInfo.ConvertTimeFromUtc(o.Timestamp.Value.ToDateTime(), SouthAfricanTimeZone);
                    return orderDateLocal >= StartDate && orderDateLocal <= EndDate;
                }).ToList();

                var analytics = ComputeAnalytics(filteredOrders);

                TotalRevenue = analytics.TotalRevenue;
                TotalOrders = analytics.TotalOrders;
                OrdersThisMonth = analytics.OrdersThisMonth;
                OrdersToday = analytics.OrdersToday;
                OrdersThisWeek = analytics.OrdersThisWeek;
                NewOrdersCount = analytics.NewOrdersCount;
                ProcessingOrdersCount = analytics.ProcessingOrdersCount;
                CompletedOrdersCount = analytics.CompletedOrdersCount;
                RecentOrders = analytics.RecentOrders;
                OrdersChartData = analytics.OrdersChartData;

                _logger.LogInformation("Dashboard loaded for {DateRange}: Revenue={Revenue}, Orders={Orders}",
                    DateRange, TotalRevenue, TotalOrders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard analytics load failed");
                SetDefaultValues();
            }
        }

        private DashboardAnalytics ComputeAnalytics(List<POSTransaction> filteredOrders)
        {
            var analytics = new DashboardAnalytics
            {
                TotalRevenue = filteredOrders.Sum(o => o.GrandTotal),
                TotalOrders = filteredOrders.Count
            };

            analytics.NewOrdersCount = filteredOrders.Count(o => o.OrderStatus == OnlineOrderStatus.New);
            analytics.ProcessingOrdersCount = filteredOrders.Count(o =>
                o.OrderStatus == OnlineOrderStatus.Processing ||
                o.OrderStatus == OnlineOrderStatus.Accepted);
            analytics.CompletedOrdersCount = filteredOrders.Count(o =>
                o.OrderStatus == OnlineOrderStatus.Completed ||
                o.OrderStatus == OnlineOrderStatus.ReadyForCollection);

            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SouthAfricanTimeZone);
            var today = localNow.Date;
            var thisWeekStart = today.AddDays(-(int)today.DayOfWeek);
            var thisMonthStart = new DateTime(today.Year, today.Month, 1);

            analytics.OrdersToday = filteredOrders.Count(o =>
            {
                if (o.Timestamp == null) return false;
                var d = TimeZoneInfo.ConvertTimeFromUtc(o.Timestamp.Value.ToDateTime(), SouthAfricanTimeZone).Date;
                return d == today;
            });

            analytics.OrdersThisWeek = filteredOrders.Count(o =>
            {
                if (o.Timestamp == null) return false;
                var d = TimeZoneInfo.ConvertTimeFromUtc(o.Timestamp.Value.ToDateTime(), SouthAfricanTimeZone).Date;
                return d >= thisWeekStart;
            });

            analytics.OrdersThisMonth = filteredOrders.Count(o =>
            {
                if (o.Timestamp == null) return false;
                var d = TimeZoneInfo.ConvertTimeFromUtc(o.Timestamp.Value.ToDateTime(), SouthAfricanTimeZone).Date;
                return d >= thisMonthStart;
            });

            analytics.RecentOrders = filteredOrders
                .OrderByDescending(o => o.Timestamp?.ToDateTime())
                .Take(5)
                .ToList();

            analytics.OrdersChartData = GenerateDynamicChartData(filteredOrders);

            return analytics;
        }

        private List<DailyOrderStats> GenerateDynamicChartData(List<POSTransaction> orders)
        {
            var span = EndDate - StartDate;

            if (span.TotalDays <= 1) return GenerateHourlyChartData(orders);

            if (span.TotalDays <= 31)
            {
                var list = new List<DailyOrderStats>();
                for (var d = StartDate.Date; d <= EndDate.Date; d = d.AddDays(1))
                {
                    var dayOrders = orders.Where(o =>
                    {
                        if (o.Timestamp == null) return false;
                        var ld = TimeZoneInfo.ConvertTimeFromUtc(o.Timestamp.Value.ToDateTime(), SouthAfricanTimeZone).Date;
                        return ld == d;
                    }).ToList();

                    list.Add(new DailyOrderStats
                    {
                        Date = d.ToString("dd MMM"),
                        OrderCount = dayOrders.Count,
                        Revenue = dayOrders.Sum(o => o.GrandTotal)
                    });
                }
                return list;
            }

            if (span.TotalDays <= 90) return GenerateWeeklyChartData(orders);
            return GenerateMonthlyChartData(orders);
        }

        private List<DailyOrderStats> GenerateHourlyChartData(List<POSTransaction> orders)
        {
            var list = new List<DailyOrderStats>();
            var targetDate = StartDate.Date;

            for (int hour = 0; hour < 24; hour++)
            {
                var hourOrders = orders.Where(o =>
                {
                    if (o.Timestamp == null) return false;
                    var t = TimeZoneInfo.ConvertTimeFromUtc(o.Timestamp.Value.ToDateTime(), SouthAfricanTimeZone);
                    return t.Date == targetDate && t.Hour == hour;
                }).ToList();

                list.Add(new DailyOrderStats
                {
                    Date = $"{hour:00}:00",
                    OrderCount = hourOrders.Count,
                    Revenue = hourOrders.Sum(o => o.GrandTotal)
                });
            }

            return list;
        }

        private List<DailyOrderStats> GenerateWeeklyChartData(List<POSTransaction> orders)
        {
            var list = new List<DailyOrderStats>();

            var groups = orders
                .Where(o => o.Timestamp != null)
                .GroupBy(o =>
                {
                    var d = TimeZoneInfo.ConvertTimeFromUtc(o.Timestamp.Value.ToDateTime(), SouthAfricanTimeZone).Date;
                    return d.AddDays(-(int)d.DayOfWeek);
                })
                .OrderBy(g => g.Key);

            foreach (var g in groups)
            {
                list.Add(new DailyOrderStats
                {
                    Date = $"Week of {g.Key:dd MMM}",
                    OrderCount = g.Count(),
                    Revenue = g.Sum(o => o.GrandTotal)
                });
            }
            return list;
        }

        private List<DailyOrderStats> GenerateMonthlyChartData(List<POSTransaction> orders)
        {
            var list = new List<DailyOrderStats>();

            var groups = orders
                .Where(o => o.Timestamp != null)
                .GroupBy(o =>
                {
                    var d = TimeZoneInfo.ConvertTimeFromUtc(o.Timestamp.Value.ToDateTime(), SouthAfricanTimeZone).Date;
                    return new DateTime(d.Year, d.Month, 1);
                })
                .OrderBy(g => g.Key);

            foreach (var g in groups)
            {
                list.Add(new DailyOrderStats
                {
                    Date = g.Key.ToString("MMM yyyy"),
                    OrderCount = g.Count(),
                    Revenue = g.Sum(o => o.GrandTotal)
                });
            }
            return list;
        }

        private async Task LoadCustomerSegmentationLifetimeAsync()
        {
            try
            {
                var allOrders = await _dbService.GetAllOrdersAsync(); // LIFETIME
                var allUsers = await _dbService.GetAllDocumentsAsync<UserAccount>("users");
                var segmentation = await ComputeCustomerSegmentation(allOrders, allUsers);

                NewBuyersCount = segmentation.NewBuyersCount;
                FrequentBuyersCount = segmentation.FrequentBuyersCount;
                NewBuyersPercentage = segmentation.NewBuyersPercentage;
                FrequentBuyersPercentage = segmentation.FrequentBuyersPercentage;
                NewBuyersList = segmentation.NewBuyersList;
                FrequentBuyersList = segmentation.FrequentBuyersList;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load lifetime customer segmentation");
                NewBuyersCount = 0;
                FrequentBuyersCount = 0;
                NewBuyersPercentage = 0;
                FrequentBuyersPercentage = 0;
                NewBuyersList = new();
                FrequentBuyersList = new();
            }
        }

        private async Task<CustomerSegmentation> ComputeCustomerSegmentation(
            List<POSTransaction> allOrders,
            List<UserAccount> allUsers)
        {
            try
            {
                var map = new Dictionary<string, CustomerOrderInfo>(StringComparer.OrdinalIgnoreCase);

                foreach (var order in allOrders)
                {
                    if (string.IsNullOrWhiteSpace(order.ClientId)) continue;

                    var key = order.ClientId.Trim().ToLowerInvariant();

                    var user = allUsers.FirstOrDefault(u =>
                        string.Equals(u.Email?.Trim(), order.ClientId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(u.Id?.Trim(), order.ClientId, StringComparison.OrdinalIgnoreCase));

                    var name = !string.IsNullOrEmpty(user?.Name)
                        ? user!.Name
                        : !string.IsNullOrEmpty(order.ClientName)
                            ? order.ClientName!
                            : (key.Contains("@") ? key.Split('@')[0] : key);

                    if (!map.TryGetValue(key, out var info))
                    {
                        info = new CustomerOrderInfo
                        {
                            CustomerId = key,
                            CustomerName = name,
                            CustomerEmail = user?.Email ?? order.ClientId,
                            OrderCount = 0,
                            TotalSpent = 0,
                            FirstOrderDate = DateTime.MaxValue,
                            LastOrderDate = DateTime.MinValue
                        };
                        map[key] = info;
                    }

                    info.OrderCount += 1;
                    info.TotalSpent += order.GrandTotal;

                    var odt = order.Timestamp?.ToDateTime() ?? DateTime.MinValue;
                    if (odt != DateTime.MinValue)
                    {
                        if (odt < info.FirstOrderDate) info.FirstOrderDate = odt;
                        if (odt > info.LastOrderDate) info.LastOrderDate = odt;
                    }
                }

                var newBuyers = new List<CustomerSegmentData>();
                var frequentBuyers = new List<CustomerSegmentData>();

                foreach (var c in map.Values)
                {
                    var data = new CustomerSegmentData
                    {
                        CustomerName = c.CustomerName,
                        CustomerEmail = c.CustomerEmail,
                        OrderCount = c.OrderCount,
                        TotalSpent = c.TotalSpent,
                        FirstOrderDate = c.FirstOrderDate == DateTime.MaxValue ? DateTime.MinValue : c.FirstOrderDate,
                        LastOrderDate = c.LastOrderDate == DateTime.MinValue ? DateTime.MinValue : c.LastOrderDate,
                        CustomerType = c.OrderCount == 1 ? "New Buyer" : "Frequent Buyer"
                    };

                    if (c.OrderCount == 1) newBuyers.Add(data);
                    else frequentBuyers.Add(data);
                }

                var totalCustomers = newBuyers.Count + frequentBuyers.Count;
                var newPct = totalCustomers > 0 ? (double)newBuyers.Count / totalCustomers * 100 : 0;
                var freqPct = totalCustomers > 0 ? (double)frequentBuyers.Count / totalCustomers * 100 : 0;

                return new CustomerSegmentation
                {
                    NewBuyersCount = newBuyers.Count,
                    FrequentBuyersCount = frequentBuyers.Count,
                    NewBuyersPercentage = newPct,
                    FrequentBuyersPercentage = freqPct,
                    NewBuyersList = newBuyers.OrderByDescending(x => x.TotalSpent).ToList(),
                    FrequentBuyersList = frequentBuyers.OrderByDescending(x => x.TotalSpent).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Customer segmentation calculation failed");
                return new CustomerSegmentation();
            }
        }

        private void SetDefaultValues()
        {
            TotalRevenue = 0;
            TotalOrders = 0;
            OrdersThisMonth = 0;
            NewOrdersCount = 0;
            ProcessingOrdersCount = 0;
            CompletedOrdersCount = 0;
            NewBuyersCount = 0;
            FrequentBuyersCount = 0;
            NewBuyersPercentage = 0;
            FrequentBuyersPercentage = 0;
            OrdersToday = 0;
            OrdersThisWeek = 0;
            RecentOrders = new();
            OrdersChartData = new();
        }

        public string GetLocalTime(Google.Cloud.Firestore.Timestamp? timestamp)
        {
            if (timestamp == null) return "-";
            var utc = timestamp.Value.ToDateTime();
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, SouthAfricanTimeZone);
            return local.ToString("dd/MM/yyyy HH:mm");
        }

        public async Task<JsonResult> OnGetGetUnreadMessagesCountAsync()
        {
            try
            {
                var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
                var isAdmin = User.HasClaim("isAdmin", "true");
                if (string.IsNullOrEmpty(userEmail))
                {
                    _logger.LogWarning("Unread messages request from unauthenticated user");
                    return new JsonResult(new { count = 0 });
                }

                var allMessages = await _dbService.GetAllDocumentsAsync<ContactMessage>("messages");
                int unreadCount;

                if (isAdmin)
                {
                    unreadCount = allMessages.Count(m =>
                        !m.IsRead &&
                        (m.MessageScope == MessageScope.AdminOnly || string.IsNullOrEmpty(m.MessageScope)));
                }
                else
                {
                    unreadCount = allMessages.Count(m =>
                        !m.IsRead &&
                        m.MessageScope != MessageScope.AdminOnly &&
                        (m.MessageScope == MessageScope.Global ||
                         (m.MessageScope == MessageScope.Customer &&
                          string.Equals(m.TargetUserId, userEmail, StringComparison.OrdinalIgnoreCase))));
                }

                return new JsonResult(new { count = unreadCount });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve unread messages count");
                return new JsonResult(new { count = 0 });
            }
        }

        public async Task<JsonResult> OnPostCheckPosStatusAsync()
        {
            try
            {
                var config = await _dbService.GetBranchConfigurationAsync();

                if (config?.PosIntegrationEnabled == true)
                {
                    var branchId = config.BranchId ?? "default_branch";
                    var collectionPath = $"onlinesale/{branchId}/transaction";

                    try
                    {
                        var collection = _dbService.GetCollection(collectionPath);
                        var snapshot = await collection.Limit(1).GetSnapshotAsync();

                        return new JsonResult(new
                        {
                            enabled = true,
                            message = $"POS integration active for branch '{branchId}'"
                        });
                    }
                    catch
                    {
                        return new JsonResult(new
                        {
                            enabled = false,
                            message = $"POS integration enabled but collection unavailable: {collectionPath}"
                        });
                    }
                }
                else
                {
                    return new JsonResult(new
                    {
                        enabled = false,
                        message = "POS integration disabled in system settings"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "POS status check failed");
                return new JsonResult(new
                {
                    enabled = false,
                    message = "Error checking POS status: " + ex.Message
                });
            }
        }

        // Lifetime segment details endpoint
        public async Task<JsonResult> OnGetCustomerSegmentDetailsAsync(string segmentType)
        {
            try
            {
                var allOrders = await _dbService.GetAllOrdersAsync(); // lifetime
                var allUsers = await _dbService.GetAllDocumentsAsync<UserAccount>("users");
                var segmentation = await ComputeCustomerSegmentation(allOrders, allUsers);

                var customers = segmentType?.Trim().ToLowerInvariant() switch
                {
                    "new" => segmentation.NewBuyersList,
                    "frequent" => segmentation.FrequentBuyersList,
                    _ => new List<CustomerSegmentData>()
                };

                return new JsonResult(new { success = true, customers = customers.Take(50) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve customer segment: {SegmentType}", segmentType);
                return new JsonResult(new { success = false, error = ex.Message });
            }
        }

        // Helper types
        private class DashboardAnalytics
        {
            public double TotalRevenue { get; set; }
            public int TotalOrders { get; set; }
            public int OrdersThisMonth { get; set; }
            public int OrdersToday { get; set; }
            public int OrdersThisWeek { get; set; }
            public int NewOrdersCount { get; set; }
            public int ProcessingOrdersCount { get; set; }
            public int CompletedOrdersCount { get; set; }
            public List<POSTransaction> RecentOrders { get; set; }
            public List<DailyOrderStats> OrdersChartData { get; set; }
        }

        private class CustomerSegmentation
        {
            public int NewBuyersCount { get; set; }
            public int FrequentBuyersCount { get; set; }
            public double NewBuyersPercentage { get; set; }
            public double FrequentBuyersPercentage { get; set; }
            public List<CustomerSegmentData> NewBuyersList { get; set; } = new();
            public List<CustomerSegmentData> FrequentBuyersList { get; set; } = new();
        }

        public class DailyOrderStats
        {
            public string Date { get; set; }
            public int OrderCount { get; set; }
            public double Revenue { get; set; }
        }

        public class CustomerSegmentData
        {
            public string CustomerName { get; set; }
            public string CustomerEmail { get; set; }
            public int OrderCount { get; set; }
            public double TotalSpent { get; set; }
            public DateTime FirstOrderDate { get; set; }
            public DateTime LastOrderDate { get; set; }
            public string CustomerType { get; set; }
            public string DisplayName =>
                !string.IsNullOrEmpty(CustomerName) ? CustomerName :
                !string.IsNullOrEmpty(CustomerEmail) ? CustomerEmail.Split('@')[0] : "Unknown";
        }

        private class CustomerOrderInfo
        {
            public string CustomerId { get; set; }
            public string CustomerName { get; set; }
            public string CustomerEmail { get; set; }
            public int OrderCount { get; set; }
            public double TotalSpent { get; set; }
            public DateTime FirstOrderDate { get; set; }
            public DateTime LastOrderDate { get; set; }
        }
    }
}
