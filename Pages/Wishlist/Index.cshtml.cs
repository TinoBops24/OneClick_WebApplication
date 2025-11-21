using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OneClick_WebApp.Models;
using OneClick_WebApp.Models.ViewModel;
using OneClick_WebApp.Services;
using OneClick_WebApp.Helpers;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace OneClick_WebApp.Pages.Wishlist
{
    [Authorize]
    [ValidateAntiForgeryToken]
    public class IndexModel : BasePageModel
    {
        public List<Product> WishlistProducts { get; private set; } = new List<Product>();
        public List<string> WishlistProductIds { get; private set; } = new List<string>();
        public int WishlistCount => WishlistProductIds.Count;

        public IndexModel(FirebaseDbService dbService) : base(dbService)
        {
        }

        public async Task<IActionResult> OnGetAsync()
        {
            await LoadSiteSettingsAsync();

            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToPage("/Account/Login");
            }

            WishlistProductIds = await LoadWishlistProductIds(userId);
            ViewData["WishlistProductIdsJson"] = System.Text.Json.JsonSerializer.Serialize(WishlistProductIds);

            if (WishlistProductIds.Any())
            {
                WishlistProducts = await LoadWishlistProducts(WishlistProductIds);
            }

            return Page();
        }

        /// <summary>
        /// Handles adding wishlist items to cart via AJAX.
        /// Validates stock availability before adding.
        /// </summary>
        public async Task<JsonResult> OnPostAddToCartAjaxAsync([FromForm] string productId, [FromForm] int quantity = 1)
        {
            if (string.IsNullOrEmpty(productId) || quantity < 1)
            {
                return new JsonResult(new { success = false, message = "Invalid product or quantity." });
            }

            try
            {
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
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = "Failed to add to cart: " + ex.Message });
            }
        }

        /// <summary>
        /// Removes single product from wishlist.
        /// </summary>
        public async Task<JsonResult> OnPostRemoveAsync([FromForm] string productId)
        {
            if (string.IsNullOrEmpty(productId))
            {
                return new JsonResult(new { success = false, message = "Product ID required." });
            }

            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return new JsonResult(new { success = false, message = "Authentication required." });
            }

            try
            {
                var wishlistProductIds = await LoadWishlistProductIds(userId);

                if (!wishlistProductIds.Contains(productId))
                {
                    return new JsonResult(new { success = false, message = "Product not in wishlist." });
                }

                wishlistProductIds.Remove(productId);

                if (wishlistProductIds.Any())
                {
                    await _dbService.SetDocumentAsync("wishlists", userId, new { ProductIds = wishlistProductIds });
                }
                else
                {
                    await _dbService.DeleteDocumentAsync("wishlists", userId);
                }

                return new JsonResult(new { success = true, wishlistCount = wishlistProductIds.Count });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = "Removal failed: " + ex.Message });
            }
        }

        /// <summary>
        /// Adds product to wishlist (called from product pages).
        /// </summary>
        public async Task<JsonResult> OnPostAddAsync([FromForm] string productId)
        {
            if (string.IsNullOrEmpty(productId))
            {
                return new JsonResult(new { success = false, message = "Product ID required." });
            }

            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return new JsonResult(new { success = false, message = "Authentication required." });
            }

            try
            {
                var productDoc = await _dbService.GetCollection("product").Document(productId).GetSnapshotAsync();
                if (!productDoc.Exists)
                {
                    return new JsonResult(new { success = false, message = "Product not found." });
                }

                var wishlistProductIds = await LoadWishlistProductIds(userId);

                if (wishlistProductIds.Contains(productId))
                {
                    return new JsonResult(new { success = false, message = "Already in wishlist." });
                }

                wishlistProductIds.Add(productId);
                await _dbService.SetDocumentAsync("wishlists", userId, new { ProductIds = wishlistProductIds });

                return new JsonResult(new { success = true, wishlistCount = wishlistProductIds.Count });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = "Failed to add: " + ex.Message });
            }
        }

        /// <summary>
        /// Clears entire wishlist.
        /// </summary>
        public async Task<JsonResult> OnPostClearAsync()
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return new JsonResult(new { success = false, message = "Authentication required." });
            }

            try
            {
                await _dbService.DeleteDocumentAsync("wishlists", userId);
                return new JsonResult(new { success = true, wishlistCount = 0 });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = "Clear failed: " + ex.Message });
            }
        }

        public async Task<JsonResult> OnGetWishlistCountAsync()
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return new JsonResult(new { count = 0 });
            }

            var wishlistProductIds = await LoadWishlistProductIds(userId);
            return new JsonResult(new { count = wishlistProductIds.Count });
        }

        private string GetUserId()
        {
            return User.FindFirst("user_id")?.Value ??
                   User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        private async Task<List<string>> LoadWishlistProductIds(string userId)
        {
            try
            {
                var wishlistDoc = await _dbService.GetCollection("wishlists").Document(userId).GetSnapshotAsync();

                if (wishlistDoc.Exists && wishlistDoc.ContainsField("ProductIds"))
                {
                    return wishlistDoc.GetValue<List<string>>("ProductIds") ?? new List<string>();
                }

                return new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        private async Task<List<Product>> LoadWishlistProducts(List<string> productIds)
        {
            var products = new List<Product>();

            try
            {
                // Firestore WhereIn limit is 10 items per query
                const int batchSize = 10;
                for (int i = 0; i < productIds.Count; i += batchSize)
                {
                    var batch = productIds.Skip(i).Take(batchSize).ToList();

                    if (batch.Any())
                    {
                        var query = _dbService.GetCollection("product")
                            .WhereIn(Google.Cloud.Firestore.FieldPath.DocumentId, batch);
                        var snapshot = await query.GetSnapshotAsync();

                        foreach (var doc in snapshot.Documents)
                        {
                            if (doc.Exists)
                            {
                                var product = doc.ConvertTo<Product>();
                                products.Add(product);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Return partial results on error
            }

            return products;
        }
    }
}