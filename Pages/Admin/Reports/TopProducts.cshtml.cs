using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using OneClick_WebApp.Models;
using OneClick_WebApp.Pages;
using OneClick_WebApp.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OneClick_WebApp.Pages.Admin.Reports
{
    // Top Products Report - Identifies best-selling products and revenue drivers for inventory optimisation
    [Authorize(Policy = "AdminOnly")]
    public class TopProductsModel : BasePageModel
    {
        private readonly ILogger<TopProductsModel> _logger;
        private readonly IMemoryCache _cache;

        private static readonly TimeZoneInfo SouthAfricanTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time");

        public TopProductsModel(
            FirebaseDbService dbService,
            IMemoryCache cache,
            ILogger<TopProductsModel> logger) : base(dbService)
        {
            _logger = logger;
            _cache = cache;
        }

        #region View Properties

        // Filter parameters
        [BindProperty(SupportsGet = true)]
        public DateTime? StartDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? EndDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string CategoryFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public int TopCount { get; set; } = 10;

        // Summary metrics
        public int TotalProductsSold { get; set; }
        public int UniqueProductsOrdered { get; set; }
        public decimal TotalRevenue { get; set; }
        public int TotalUnitsSold { get; set; }

        // Report data collections
        public List<ProductPerformance> TopProductsByRevenue { get; set; } = new();
        public List<ProductPerformance> TopProductsByQuantity { get; set; } = new();
        public List<CategoryPerformance> CategoryBreakdown { get; set; } = new();
        public List<ProductPerformance> LowPerformers { get; set; } = new();

        // Available categories for filter dropdown
        public List<string> AvailableCategories { get; set; } = new();

        #endregion

        // Main page handler
        public async Task OnGetAsync()
        {
            await LoadSiteSettingsAsync();

            // Default to last 30 days
            if (!StartDate.HasValue || !EndDate.HasValue)
            {
                EndDate = DateTime.Today;
                StartDate = EndDate.Value.AddDays(-30);
            }

            await LoadTopProductsDataAsync();
        }

        // Loads and analyses product performance data
        private async Task LoadTopProductsDataAsync()
        {
            try
            {
                _logger.LogInformation("Loading top products report from {StartDate} to {EndDate}",
                    StartDate, EndDate);

                // Get all orders with caching
                var allOrders = await _cache.GetOrCreateAsync("Admin_AllOrders_ForReports", async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                    entry.SetPriority(CacheItemPriority.High);
                    return await _dbService.GetAllOrdersAsync();
                });

                // Filter orders to date range
                var filteredOrders = allOrders
                    .Where(o => o.Timestamp.HasValue)
                    .Where(o => {
                        var localDate = ConvertToLocalDateTime(o.Timestamp.Value);
                        return localDate.Date >= StartDate.Value.Date &&
                               localDate.Date <= EndDate.Value.Date;
                    })
                    .ToList();

                _logger.LogInformation("Processing {OrderCount} orders for product analysis",
                    filteredOrders.Count);

                // Extract all stock movements (products sold) from orders
                var allStockMovements = filteredOrders
                    .Where(o => o.StockMovements != null && o.StockMovements.Any())
                    .SelectMany(o => o.StockMovements
                        .Where(sm => sm.Product != null)
                        .Select(sm => new ProductSale
                        {
                            ProductId = sm.Product.DocumentId ?? sm.Product.Id,
                            ProductName = sm.Product.Name,
                            Category = sm.Product.Category?.Name ?? "Uncategorised",
                            Quantity = sm.Quantity,
                            Revenue = (decimal)sm.LineTotal,
                            UnitPrice = (decimal)sm.UnitPrice,
                            OrderDate = ConvertToLocalDateTime(o.Timestamp.Value)
                        }))
                    .ToList();

                // Apply category filter if specified
                if (!string.IsNullOrEmpty(CategoryFilter))
                {
                    allStockMovements = allStockMovements
                        .Where(sm => sm.Category.Equals(CategoryFilter, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                // Calculate summary statistics
                CalculateSummaryStatistics(allStockMovements);

                // Generate report sections
                GenerateTopProductsByRevenue(allStockMovements);
                GenerateTopProductsByQuantity(allStockMovements);
                GenerateCategoryBreakdown(allStockMovements);
                IdentifyLowPerformers(allStockMovements);

                // Extract unique categories for filter dropdown
                AvailableCategories = allStockMovements
                    .Select(sm => sm.Category)
                    .Distinct()
                    .OrderBy(c => c)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load top products data");
                // Set safe defaults
                TotalProductsSold = 0;
                TotalRevenue = 0;
            }
        }

        // Calculates summary statistics for the period
        private void CalculateSummaryStatistics(List<ProductSale> sales)
        {
            TotalProductsSold = sales.Count;
            UniqueProductsOrdered = sales.Select(s => s.ProductId).Distinct().Count();
            TotalRevenue = sales.Sum(s => s.Revenue);
            TotalUnitsSold = sales.Sum(s => s.Quantity);
        }

        // Generates top products ranked by total revenue
        private void GenerateTopProductsByRevenue(List<ProductSale> sales)
        {
            TopProductsByRevenue = sales
                .GroupBy(s => new { s.ProductId, s.ProductName, s.Category })
                .Select(g => new ProductPerformance
                {
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.ProductName,
                    Category = g.Key.Category,
                    TotalRevenue = g.Sum(s => s.Revenue),
                    TotalQuantitySold = g.Sum(s => s.Quantity),
                    OrderCount = g.Count(),
                    AveragePrice = g.Average(s => s.UnitPrice),
                    RevenuePercentage = TotalRevenue > 0
                        ? (g.Sum(s => s.Revenue) / TotalRevenue) * 100
                        : 0
                })
                .OrderByDescending(p => p.TotalRevenue)
                .Take(TopCount)
                .ToList();
        }

        // Generates top products ranked by quantity sold
        private void GenerateTopProductsByQuantity(List<ProductSale> sales)
        {
            TopProductsByQuantity = sales
                .GroupBy(s => new { s.ProductId, s.ProductName, s.Category })
                .Select(g => new ProductPerformance
                {
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.ProductName,
                    Category = g.Key.Category,
                    TotalRevenue = g.Sum(s => s.Revenue),
                    TotalQuantitySold = g.Sum(s => s.Quantity),
                    OrderCount = g.Count(),
                    AveragePrice = g.Average(s => s.UnitPrice),
                    RevenuePercentage = TotalRevenue > 0
                        ? (g.Sum(s => s.Revenue) / TotalRevenue) * 100
                        : 0
                })
                .OrderByDescending(p => p.TotalQuantitySold)
                .Take(TopCount)
                .ToList();
        }

        // Generates category-level performance breakdown
        private void GenerateCategoryBreakdown(List<ProductSale> sales)
        {
            CategoryBreakdown = sales
                .GroupBy(s => s.Category)
                .Select(g => new CategoryPerformance
                {
                    CategoryName = g.Key,
                    TotalRevenue = g.Sum(s => s.Revenue),
                    TotalQuantitySold = g.Sum(s => s.Quantity),
                    UniqueProducts = g.Select(s => s.ProductId).Distinct().Count(),
                    AverageOrderValue = g.Any() ? g.Average(s => s.Revenue) : 0,
                    RevenuePercentage = TotalRevenue > 0
                        ? (g.Sum(s => s.Revenue) / TotalRevenue) * 100
                        : 0
                })
                .OrderByDescending(c => c.TotalRevenue)
                .ToList();
        }

        // Identifies low-performing products that may need attention
        private void IdentifyLowPerformers(List<ProductSale> sales)
        {
            var allProducts = sales
                .GroupBy(s => new { s.ProductId, s.ProductName, s.Category })
                .Select(g => new ProductPerformance
                {
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.ProductName,
                    Category = g.Key.Category,
                    TotalRevenue = g.Sum(s => s.Revenue),
                    TotalQuantitySold = g.Sum(s => s.Quantity),
                    OrderCount = g.Count(),
                    AveragePrice = g.Average(s => s.UnitPrice),
                    RevenuePercentage = TotalRevenue > 0
                        ? (g.Sum(s => s.Revenue) / TotalRevenue) * 100
                        : 0
                })
                .ToList();

            // Low performers: products with < 5 units sold or < R500 revenue in the period
            LowPerformers = allProducts
                .Where(p => p.TotalQuantitySold < 5 || p.TotalRevenue < 500)
                .OrderBy(p => p.TotalRevenue)
                .Take(10)
                .ToList();
        }

        // Converts UTC timestamp to South African local time
        private DateTime ConvertToLocalDateTime(Google.Cloud.Firestore.Timestamp timestamp)
        {
            var utcDateTime = timestamp.ToDateTime();
            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, SouthAfricanTimeZone);
        }

        #region Helper Classes

        // Represents a single product sale transaction
        private class ProductSale
        {
            public string ProductId { get; set; }
            public string ProductName { get; set; }
            public string Category { get; set; }
            public int Quantity { get; set; }
            public decimal Revenue { get; set; }
            public decimal UnitPrice { get; set; }
            public DateTime OrderDate { get; set; }
        }

        // Represents aggregated product performance metrics
        public class ProductPerformance
        {
            public string ProductId { get; set; }
            public string ProductName { get; set; }
            public string Category { get; set; }
            public decimal TotalRevenue { get; set; }
            public int TotalQuantitySold { get; set; }
            public int OrderCount { get; set; }
            public decimal AveragePrice { get; set; }
            public decimal RevenuePercentage { get; set; }
        }

        // Represents category-level performance metrics
        public class CategoryPerformance
        {
            public string CategoryName { get; set; }
            public decimal TotalRevenue { get; set; }
            public int TotalQuantitySold { get; set; }
            public int UniqueProducts { get; set; }
            public decimal AverageOrderValue { get; set; }
            public decimal RevenuePercentage { get; set; }
        }

        #endregion
    }
}