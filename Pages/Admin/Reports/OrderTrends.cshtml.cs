using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using OneClick_WebApp.Models;
using OneClick_WebApp.Models.Enums;
using OneClick_WebApp.Pages;
using OneClick_WebApp.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OneClick_WebApp.Pages.Admin.Reports
{
    // Order Trends Report - Comprehensive analysis of order patterns and revenue trends for business intelligence
    [Authorize(Policy = "AdminOnly")]
    public class OrderTrendsModel : BasePageModel
    {
        private readonly ILogger<OrderTrendsModel> _logger;
        private readonly IMemoryCache _cache;

        private const string TrendsCacheKey = "Admin_OrderTrends_Data";

        // South African timezone for accurate local date calculations
        private static readonly TimeZoneInfo SouthAfricanTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time");

        public OrderTrendsModel(
            FirebaseDbService dbService,
            IMemoryCache cache,
            ILogger<OrderTrendsModel> logger) : base(dbService)
        {
            _logger = logger;
            _cache = cache;
        }

        #region View Properties

        // Filter parameters for customising report timeframe
        [BindProperty(SupportsGet = true)]
        public DateTime? StartDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? EndDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string GroupBy { get; set; } = "day";

        // Summary statistics displayed in metric cards
        public int TotalOrders { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal AverageOrderValue { get; set; }
        public int TotalCustomers { get; set; }

        // Growth metrics comparing current period to previous period
        public decimal PreviousPeriodRevenue { get; set; }
        public decimal RevenueGrowthPercentage { get; set; }
        public int PreviousPeriodOrders { get; set; }
        public decimal OrderGrowthPercentage { get; set; }

        // Chart data collections
        public List<TrendDataPoint> DailyTrends { get; set; } = new();
        public List<MonthlyComparison> MonthlyData { get; set; } = new();
        public List<StatusBreakdown> OrdersByStatus { get; set; } = new();
        public List<HourlyPattern> PeakHours { get; set; } = new();

        // Top performance metrics for highlighting
        public decimal HighestDailyRevenue { get; set; }
        public string HighestRevenueDate { get; set; }
        public int BusiestDayOrderCount { get; set; }
        public string BusiestDay { get; set; }

        #endregion

        // Main page handler - loads report with filtered date range
        public async Task OnGetAsync()
        {
            await LoadSiteSettingsAsync();

            // Default to last 30 days if no dates specified
            if (!StartDate.HasValue || !EndDate.HasValue)
            {
                EndDate = DateTime.Today;
                StartDate = EndDate.Value.AddDays(-30);
            }

            await LoadOrderTrendsDataAsync();
        }

        // Loads and processes order data with caching for performance optimisation
        private async Task LoadOrderTrendsDataAsync()
        {
            try
            {
                _logger.LogInformation("Loading order trends from {StartDate} to {EndDate}",
                    StartDate, EndDate);

                // Retrieve all orders with 5-minute cache
                var allOrders = await _cache.GetOrCreateAsync("Admin_AllOrders_ForReports", async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                    entry.SetPriority(CacheItemPriority.High);
                    return await _dbService.GetAllOrdersAsync();
                });

                // Filter orders to selected date range and convert to proper type
                var filteredOrders = allOrders
                    .Where(o => o.Timestamp.HasValue)
                    .Select(o => new OrderWithLocalDate
                    {
                        Order = o,
                        LocalDate = ConvertToLocalDateTime(o.Timestamp.Value)
                    })
                    .Where(x => x.LocalDate.Date >= StartDate.Value.Date &&
                                x.LocalDate.Date <= EndDate.Value.Date)
                    .ToList();

                _logger.LogInformation("Found {OrderCount} orders in selected date range",
                    filteredOrders.Count);

                // Calculate all report metrics
                CalculateSummaryStatistics(filteredOrders);
                await CalculateGrowthMetricsAsync(allOrders, filteredOrders);
                GenerateDailyTrends(filteredOrders);
                GenerateMonthlyComparisons(filteredOrders);
                GenerateStatusBreakdown(filteredOrders);
                GeneratePeakHoursAnalysis(filteredOrders);
                IdentifyTopPerformingDays(filteredOrders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load order trends data");
                // Set safe defaults to prevent page errors
                TotalOrders = 0;
                TotalRevenue = 0;
                AverageOrderValue = 0;
            }
        }

        // Calculates summary statistics for metric cards
        private void CalculateSummaryStatistics(List<OrderWithLocalDate> orders)
        {
            TotalOrders = orders.Count;
            TotalRevenue = (decimal)orders.Sum(x => x.Order.GrandTotal);
            AverageOrderValue = TotalOrders > 0 ? TotalRevenue / TotalOrders : 0;

            // Count unique customers in the period
            TotalCustomers = orders
                .Select(x => x.Order.ClientId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .Count();
        }

        // Calculates growth by comparing to previous period of same length
        private async Task CalculateGrowthMetricsAsync(
            List<POSTransaction> allOrders,
            List<OrderWithLocalDate> currentPeriodOrders)
        {
            try
            {
                // Calculate previous period dates
                var periodLength = (EndDate.Value - StartDate.Value).Days;
                var previousStart = StartDate.Value.AddDays(-periodLength - 1);
                var previousEnd = StartDate.Value.AddDays(-1);

                // Get previous period orders
                var previousOrders = allOrders
                    .Where(o => o.Timestamp.HasValue)
                    .Select(o => new OrderWithLocalDate
                    {
                        Order = o,
                        LocalDate = ConvertToLocalDateTime(o.Timestamp.Value)
                    })
                    .Where(x => x.LocalDate.Date >= previousStart.Date &&
                                x.LocalDate.Date <= previousEnd.Date)
                    .ToList();

                PreviousPeriodOrders = previousOrders.Count;
                PreviousPeriodRevenue = (decimal)previousOrders.Sum(x => x.Order.GrandTotal);

                // Calculate growth percentages
                if (PreviousPeriodRevenue > 0)
                {
                    RevenueGrowthPercentage =
                        ((TotalRevenue - PreviousPeriodRevenue) / PreviousPeriodRevenue) * 100;
                }

                if (PreviousPeriodOrders > 0)
                {
                    OrderGrowthPercentage =
                        ((decimal)(TotalOrders - PreviousPeriodOrders) / PreviousPeriodOrders) * 100;
                }

                _logger.LogInformation(
                    "Growth calculated - Revenue: {RevenueGrowth}%, Orders: {OrderGrowth}%",
                    RevenueGrowthPercentage.ToString("F1"),
                    OrderGrowthPercentage.ToString("F1"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not calculate growth metrics");
                RevenueGrowthPercentage = 0;
                OrderGrowthPercentage = 0;
            }
        }

        // Generates daily trend data for line chart
        private void GenerateDailyTrends(List<OrderWithLocalDate> orders)
        {
            DailyTrends = orders
                .GroupBy(x => x.LocalDate.Date)
                .OrderBy(g => g.Key)
                .Select(g => new TrendDataPoint
                {
                    Date = g.Key.ToString("dd MMM"),
                    FullDate = g.Key,
                    OrderCount = g.Count(),
                    Revenue = (decimal)g.Sum(x => x.Order.GrandTotal),
                    AverageOrderValue = (decimal)g.Average(x => x.Order.GrandTotal)
                })
                .ToList();
        }

        // Generates monthly aggregated data for comparison chart
        private void GenerateMonthlyComparisons(List<OrderWithLocalDate> orders)
        {
            MonthlyData = orders
                .GroupBy(x => new
                {
                    Year = x.LocalDate.Year,
                    Month = x.LocalDate.Month
                })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new MonthlyComparison
                {
                    MonthName = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                    OrderCount = g.Count(),
                    Revenue = (decimal)g.Sum(x => x.Order.GrandTotal)
                })
                .ToList();
        }

        // Generates order status distribution for pie chart
        private void GenerateStatusBreakdown(List<OrderWithLocalDate> orders)
        {
            OrdersByStatus = orders
                .GroupBy(x => x.Order.OrderStatus)
                .Select(g => new StatusBreakdown
                {
                    Status = g.Key.ToString(),
                    Count = g.Count(),
                    Percentage = TotalOrders > 0
                        ? (decimal)g.Count() / TotalOrders * 100
                        : 0,
                    Revenue = (decimal)g.Sum(x => x.Order.GrandTotal)
                })
                .OrderByDescending(s => s.Count)
                .ToList();
        }

        // Analyses peak ordering hours for operational insights
        private void GeneratePeakHoursAnalysis(List<OrderWithLocalDate> orders)
        {
            PeakHours = orders
                .GroupBy(x => x.LocalDate.Hour)
                .Select(g => new HourlyPattern
                {
                    Hour = g.Key,
                    HourLabel = $"{g.Key:00}:00",
                    OrderCount = g.Count(),
                    Revenue = (decimal)g.Sum(x => x.Order.GrandTotal)
                })
                .OrderBy(h => h.Hour)
                .ToList();
        }

        // Identifies highest performing days for report highlights
        private void IdentifyTopPerformingDays(List<OrderWithLocalDate> orders)
        {
            var dailyStats = orders
                .GroupBy(x => x.LocalDate.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    OrderCount = g.Count(),
                    Revenue = (decimal)g.Sum(x => x.Order.GrandTotal)
                })
                .ToList();

            if (dailyStats.Any())
            {
                var topRevenueDay = dailyStats.OrderByDescending(d => d.Revenue).First();
                HighestDailyRevenue = topRevenueDay.Revenue;
                HighestRevenueDate = topRevenueDay.Date.ToString("dd MMM yyyy");

                var busiestDay = dailyStats.OrderByDescending(d => d.OrderCount).First();
                BusiestDayOrderCount = busiestDay.OrderCount;
                BusiestDay = busiestDay.Date.ToString("dd MMM yyyy");
            }
        }

        // Converts UTC Firestore timestamp to South African local time
        private DateTime ConvertToLocalDateTime(Google.Cloud.Firestore.Timestamp timestamp)
        {
            var utcDateTime = timestamp.ToDateTime();
            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, SouthAfricanTimeZone);
        }

        #region Helper Classes

        // Helper class to store order with local date - fixes type conversion issue
        private class OrderWithLocalDate
        {
            public POSTransaction Order { get; set; }
            public DateTime LocalDate { get; set; }
        }

        // Data point for daily trends chart
        public class TrendDataPoint
        {
            public string Date { get; set; }
            public DateTime FullDate { get; set; }
            public int OrderCount { get; set; }
            public decimal Revenue { get; set; }
            public decimal AverageOrderValue { get; set; }
        }

        // Monthly aggregated comparison data
        public class MonthlyComparison
        {
            public string MonthName { get; set; }
            public int OrderCount { get; set; }
            public decimal Revenue { get; set; }
        }

        // Order status distribution data
        public class StatusBreakdown
        {
            public string Status { get; set; }
            public int Count { get; set; }
            public decimal Percentage { get; set; }
            public decimal Revenue { get; set; }
        }

        // Hourly ordering pattern data
        public class HourlyPattern
        {
            public int Hour { get; set; }
            public string HourLabel { get; set; }
            public int OrderCount { get; set; }
            public decimal Revenue { get; set; }
        }

        #endregion
    }
}