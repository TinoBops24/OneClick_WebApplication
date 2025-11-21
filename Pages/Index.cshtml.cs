using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using OneClick_WebApp.Models;
using OneClick_WebApp.Models.ViewModel;
using OneClick_WebApp.Services;
using OneClick_WebApp.Helpers;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using System;

namespace OneClick_WebApp.Pages
{
    public class IndexModel : BasePageModel
    {
        private readonly IMemoryCache _cache;

        // Cache keys
        private const string FeaturedProductsCacheKey = "Homepage_FeaturedProducts";
        private const string CategoriesWithImagesCacheKey = "Homepage_CategoriesWithImages";

        public List<Product> FeaturedProducts { get; private set; } = new();
        public List<CategoryWithImage> CategoriesWithImages { get; private set; } = new();

        public IndexModel(FirebaseDbService dbService, IMemoryCache cache) : base(dbService)
        {
            _cache = cache;
        }

        public async Task OnGetAsync()
        {
            await LoadSiteSettingsAsync();

            // Load featured products from cache or database
            FeaturedProducts = await GetCachedFeaturedProductsAsync();

            // Load categories with images from cache or database
            CategoriesWithImages = await GetCachedCategoriesWithImagesAsync();
        }

        /// <summary>
        /// Retrieves featured products from cache or queries database.
        /// Cache expires after 3 minutes.
        /// </summary>
        private async Task<List<Product>> GetCachedFeaturedProductsAsync()
        {
            return await _cache.GetOrCreateAsync(FeaturedProductsCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3);
                entry.SetPriority(CacheItemPriority.High);

                var featuredQuery = _dbService.GetCollection("product")
                                              .WhereEqualTo("HideInWeb", false)
                                              .WhereEqualTo("HideInPOS", false)
                                              .Limit(8);

                var productSnapshot = await featuredQuery.GetSnapshotAsync();

                return productSnapshot.Documents
                    .Select(doc => doc.ConvertTo<Product>())
                    .Where(p => p != null)
                    .ToList();
            });
        }

        /// <summary>
        /// Retrieves categories with representative product images from cache or database.
        /// Cache expires after 5 minutes. This method consolidates multiple queries.
        /// </summary>
        private async Task<List<CategoryWithImage>> GetCachedCategoriesWithImagesAsync()
        {
            return await _cache.GetOrCreateAsync(CategoriesWithImagesCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                entry.SetPriority(CacheItemPriority.Normal);

                // Fetch categories
                var categorySnapshot = await _dbService.GetCollection("category")
                                                       .Limit(6)
                                                       .GetSnapshotAsync();

                var categories = categorySnapshot.Documents
                    .Select(doc => doc.ConvertTo<ProductCategory>())
                    .Where(c => c != null)
                    .ToList();

                var categoriesWithImages = new List<CategoryWithImage>();

                // Fetch product images for each category in parallel
                var imageTasks = categories.Select(async category =>
                {
                    var productQuery = _dbService.GetCollection("product")
                                                 .WhereEqualTo("category.ID", category.Id)
                                                 .Limit(1);

                    var productDocs = await productQuery.GetSnapshotAsync();
                    var firstProductDoc = productDocs.Documents.FirstOrDefault();

                    string imageUrl = null;
                    if (firstProductDoc != null && firstProductDoc.ContainsField("PictureAttachment"))
                    {
                        imageUrl = firstProductDoc.GetValue<string>("PictureAttachment");
                    }

                    return new CategoryWithImage
                    {
                        Id = category.Id,
                        Name = category.Name,
                        ImageUrl = imageUrl
                    };
                }).ToList();

                categoriesWithImages = (await Task.WhenAll(imageTasks)).ToList();

                return categoriesWithImages;
            });
        }

        private string GetUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier);
        }

        /// <summary>
        /// Handles adding products to cart via AJAX
        /// </summary>
        [Authorize]
        public async Task<JsonResult> OnPostAddToCartAjaxAsync(string productId, int quantity)
        {
            if (string.IsNullOrEmpty(productId) || quantity < 1)
            {
                return new JsonResult(new { success = false, message = "Invalid product or quantity." });
            }

            var doc = await _dbService.GetCollection("product").Document(productId).GetSnapshotAsync();
            if (!doc.Exists)
            {
                return new JsonResult(new { success = false, message = "Product not found." });
            }

            var product = doc.ConvertTo<Product>();
            var cart = HttpContext.Session?.GetObject<List<CartItem>>("Cart") ?? new List<CartItem>();

            var existingItem = cart.FirstOrDefault(c => c.ProductId == productId);
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

            // Persist to Firestore
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

            return new JsonResult(new
            {
                success = true,
                message = $"{product.Name} added to cart.",
                cart = cart
            });
        }
    }

    public class CategoryWithImage
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ImageUrl { get; set; }
    }
}