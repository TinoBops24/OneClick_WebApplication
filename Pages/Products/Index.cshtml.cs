using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using OneClick_WebApp.Models;
using OneClick_WebApp.Models.ViewModel;
using OneClick_WebApp.Services;
using OneClick_WebApp.Helpers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System;

namespace OneClick_WebApp.Pages.Products
{
    public class IndexModel : BasePageModel
    {
        private const int PageSize = 9;
        private static readonly string[] Branches = { "almeida", "control", "feprol", "nacala" };
        private readonly IMemoryCache _cache;
        private readonly CacheManagerService _cacheManager;

        // Cache keys for consistent caching
        private const string CategoriesCacheKey = "Products_Categories";
        private const string ProductsCacheKey = "Products_All";
        private const string StockCacheKey = "Products_Stock";

        public IndexModel(
            FirebaseDbService dbService,
            CacheManagerService cacheManager,
            IMemoryCache cache) : base(dbService)
        {
            _cache = cache;
            _cacheManager = cacheManager;
        }

        // Page data properties
        public List<ProductCategory> Categories { get; private set; } = new();
        public List<Product> Products { get; private set; } = new();
        public List<string> WishlistProductIds { get; set; } = new();
        public Dictionary<string, double> StockByProduct { get; private set; } = new();

        // Query parameters
        [BindProperty(SupportsGet = true)] public string SearchTerm { get; set; }
        [BindProperty(SupportsGet = true)] public string CategoryFilter { get; set; }
        [BindProperty(SupportsGet = true)] public decimal? MinPrice { get; set; }
        [BindProperty(SupportsGet = true)] public decimal? MaxPrice { get; set; }
        [BindProperty(SupportsGet = true)] public string SortOrder { get; set; }
        [BindProperty(SupportsGet = true)] public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; private set; }

        private string GetUserId()
        {
            return User.FindFirst("user_id")?.Value ??
                   User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        public async Task OnGetAsync()
        {
            await LoadSiteSettingsAsync();
            await LoadCartIntoSessionAsync();
            await LoadWishlistIntoSessionAsync();

            // Load wishlist from session
            var wishlistSession = HttpContext.Session?.GetString("Wishlist");
            if (!string.IsNullOrEmpty(wishlistSession))
            {
                try
                {
                    WishlistProductIds = System.Text.Json.JsonSerializer.Deserialize<List<string>>(wishlistSession) ?? new List<string>();
                }
                catch
                {
                    WishlistProductIds = new List<string>();
                }
            }

            // Load products with caching
            var allVisibleProducts = await GetCachedProductsAsync();

            // Load stock data with caching
            StockByProduct = await GetCachedStockAsync();

            // Extract categories from products (cached separately)
            Categories = await GetCachedCategoriesAsync(allVisibleProducts);

            // Apply filters in-memory
            var filteredProducts = allVisibleProducts.AsEnumerable();

            if (!string.IsNullOrEmpty(CategoryFilter))
            {
                filteredProducts = filteredProducts.Where(p =>
                    p.Category != null && p.Category.Id == CategoryFilter);
            }

            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                filteredProducts = filteredProducts.Where(p =>
                    (p.Name != null && p.Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)) ||
                    (p.Description != null && p.Description.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
                );
            }

            if (MinPrice.HasValue)
            {
                filteredProducts = filteredProducts.Where(p => p.Price >= (double)MinPrice.Value);
            }

            if (MaxPrice.HasValue)
            {
                filteredProducts = filteredProducts.Where(p => p.Price <= (double)MaxPrice.Value);
            }

            // Apply sorting
            filteredProducts = SortOrder switch
            {
                "name_asc" => filteredProducts.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
                "name_desc" => filteredProducts.OrderByDescending(p => p.Name, StringComparer.OrdinalIgnoreCase),
                "price_asc" => filteredProducts.OrderBy(p => p.Price),
                "price_desc" => filteredProducts.OrderByDescending(p => p.Price),
                _ => filteredProducts.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            };

            // Pagination
            var totalProducts = filteredProducts.Count();
            TotalPages = (int)Math.Ceiling(totalProducts / (double)PageSize);
            Products = filteredProducts
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();
        }

        // Updated to use application-level cache from CacheManagerService
        private async Task<List<Product>> GetCachedProductsAsync()
        {
            return await _cache.GetOrCreateAsync(ProductsCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
                entry.SetPriority(CacheItemPriority.High);

                // Get products from application-level cache
                var allProducts = await _cacheManager.GetProductsAsync();

                // Filter for visible products only
                var visibleProducts = allProducts
                    .Where(p => !p.HideInWeb && !p.HideInPOS)
                    .ToList();

                return visibleProducts;
            });
        }

        private async Task<List<ProductCategory>> GetCachedCategoriesAsync(List<Product> products)
        {
            return await _cache.GetOrCreateAsync(CategoriesCacheKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                entry.SetPriority(CacheItemPriority.Normal);

                var categories = products
                    .Where(p => p.Category != null && !string.IsNullOrEmpty(p.Category.Id))
                    .Select(p => p.Category)
                    .GroupBy(c => c.Id)
                    .Select(g => g.First())
                    .OrderBy(c => c.Name)
                    .ToList();

                return Task.FromResult(categories);
            });
        }

        private async Task<Dictionary<string, double>> GetCachedStockAsync()
        {
            return await _cache.GetOrCreateAsync(StockCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                entry.SetPriority(CacheItemPriority.High);

                return await LoadStockByProductAsync();
            });
        }

        private async Task<Dictionary<string, double>> LoadStockByProductAsync()
        {
            var stockDict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            var stockTasks = Branches.Select(async branch =>
            {
                var stockSnap = await _dbService
                    .GetCollection("location")
                    .Document(branch)
                    .Collection("stock")
                    .GetSnapshotAsync();

                var branchStock = new Dictionary<string, double>();

                foreach (var doc in stockSnap.Documents)
                {
                    double qty = 0;
                    if (doc.TryGetValue("Quantity", out double qDouble))
                        qty = qDouble;
                    else if (doc.TryGetValue("Quantity", out long qLong))
                        qty = qLong;

                    branchStock[doc.Id] = qty;
                }

                return branchStock;
            }).ToList();

            var results = await Task.WhenAll(stockTasks);

            // Total stock from all branches
            foreach (var branchStock in results)
            {
                foreach (var kvp in branchStock)
                {
                    stockDict.TryGetValue(kvp.Key, out double currentQty);
                    stockDict[kvp.Key] = currentQty + kvp.Value;
                }
            }

            return stockDict;
        }

        [Authorize]
        public async Task<JsonResult> OnPostAddToCartAjaxAsync(string productId, int quantity)
        {
            if (string.IsNullOrEmpty(productId) || quantity < 1)
            {
                return new JsonResult(new { success = false, message = "Invalid product or quantity." });
            }

            var currentStock = await GetCachedStockAsync();
            if (!currentStock.TryGetValue(productId, out var availableStock))
            {
                return new JsonResult(new { success = false, message = "Product not found or stock unavailable." });
            }

            var cart = HttpContext.Session?.GetObject<List<CartItem>>("Cart") ?? new List<CartItem>();
            var existingItem = cart.FirstOrDefault(c => c.ProductId == productId);
            int quantityInCart = existingItem?.Quantity ?? 0;

            if ((quantityInCart + quantity) > availableStock)
            {
                return new JsonResult(new { success = false, message = $"Only {availableStock} item(s) available in stock." });
            }

            var doc = await _dbService.GetCollection("product").Document(productId).GetSnapshotAsync();
            if (!doc.Exists)
            {
                return new JsonResult(new { success = false, message = "Product not found." });
            }

            var product = doc.ConvertTo<Product>();

            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
            }
            else
            {
                cart.Add(new CartItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    Quantity = quantity,
                    Price = (double)product.Price,
                    ImageUrl = product.ImageUrl
                });
            }

            HttpContext.Session?.SetObject("Cart", cart);

            var userId = GetUserId();
            if (!string.IsNullOrEmpty(userId))
            {
                var cartData = cart.Select(item => new Dictionary<string, object>
                {
                    { "ProductId", item.ProductId },
                    { "ProductName", item.ProductName },
                    { "Quantity", item.Quantity },
                    { "Price", item.Price },
                    { "ImageUrl", item.ImageUrl }
                }).ToList();

                await _dbService.GetCollection("carts").Document(userId).SetAsync(new { items = cartData });
            }

            return new JsonResult(new { success = true, message = $"{product.Name} added to cart.", cart = cart });
        }

        [Authorize]
        public async Task<JsonResult> OnPostToggleWishlistAsync([FromForm] string productId)
        {
            if (string.IsNullOrEmpty(productId))
            {
                return new JsonResult(new { success = false, message = "Invalid product." });
            }

            var userId = GetUserId();
            var doc = await _dbService.GetCollection("product").Document(productId).GetSnapshotAsync();

            if (!doc.Exists)
            {
                return new JsonResult(new { success = false, message = "Product not found." });
            }

            var product = doc.ConvertTo<Product>();
            var wishlistRef = _dbService.GetCollection("wishlists").Document(userId);
            var wishlistSnapshot = await wishlistRef.GetSnapshotAsync();
            List<string> productIds = new();

            if (wishlistSnapshot.Exists && wishlistSnapshot.ContainsField("ProductIds"))
            {
                productIds = wishlistSnapshot.GetValue<List<string>>("ProductIds");
            }

            bool isWishlisted;
            string message;

            if (productIds.Contains(productId))
            {
                productIds.Remove(productId);
                message = $"{product.Name} removed from wishlist.";
                isWishlisted = false;
            }
            else
            {
                productIds.Add(productId);
                message = $"{product.Name} added to wishlist.";
                isWishlisted = true;
            }

            await wishlistRef.SetAsync(new Dictionary<string, object> { { "ProductIds", productIds } });
            HttpContext.Session.SetString("Wishlist", System.Text.Json.JsonSerializer.Serialize(productIds));

            return new JsonResult(new { success = true, message, isWishlisted });
        }
    }
}