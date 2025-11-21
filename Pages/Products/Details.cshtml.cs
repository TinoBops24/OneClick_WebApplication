using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OneClick_WebApp.Models;
using OneClick_WebApp.Services;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;
using OneClick_WebApp.Helpers;
using OneClick_WebApp.Models.ViewModel;

namespace OneClick_WebApp.Pages.Products
{
    public class DetailsModel : BasePageModel
    {
        // This property will hold the product data to display on the page
        public Product Product { get; private set; }

        // This property will receive the product ID from the URL route
        [BindProperty(SupportsGet = true)]
        public string Id { get; set; }

        public DetailsModel(FirebaseDbService dbService) : base(dbService)
        {
        }

        public async Task<IActionResult> OnGetAsync()
        {
            // First, load any global site settings
            await LoadSiteSettingsAsync();

            // If no ID was provided in the URL, the page can't be found
            if (string.IsNullOrEmpty(Id))
            {
                return NotFound();
            }

            // Fetch the specific product document from Firestore using the ID
            var docRef = _dbService.GetCollection("product").Document(Id);
            var snapshot = await docRef.GetSnapshotAsync();

            // If no product with that ID exists in the database, the page can't be found
            if (!snapshot.Exists)
            {
                return NotFound();
            }

            // Convert the Firestore document into our Product model
            Product = snapshot.ConvertTo<Product>();

            // Render the page
            return Page();
        }

        // NEW: Handler for fetching related products from the same category
        public async Task<JsonResult> OnGetRelatedProductsAsync()
        {
            try
            {
                // Get the current product first to determine its category
                if (string.IsNullOrEmpty(Id))
                {
                    return new JsonResult(new { success = false, message = "Product ID is required" });
                }

                var currentProductDoc = await _dbService.GetCollection("product").Document(Id).GetSnapshotAsync();
                if (!currentProductDoc.Exists)
                {
                    return new JsonResult(new { success = false, message = "Product not found" });
                }

                var currentProduct = currentProductDoc.ConvertTo<Product>();

                // If the current product has no category, return empty results
                if (currentProduct.Category == null || string.IsNullOrEmpty(currentProduct.Category.Id))
                {
                    return new JsonResult(new { success = true, products = new List<object>() });
                }

                // Query for products in the same category, excluding the current product
                var relatedQuery = _dbService.GetCollection("product")
                    .WhereEqualTo("HideInWeb", false)
                    .WhereEqualTo("HideInPOS", false)
                    .Limit(20); // Get more than we need, then filter

                var snapshot = await relatedQuery.GetSnapshotAsync();

                var relatedProducts = snapshot.Documents
                    .Select(doc => doc.ConvertTo<Product>())
                    .Where(p => p != null &&
                               p.Id != currentProduct.Id && // Exclude current product
                               p.Category != null &&
                               p.Category.Id == currentProduct.Category.Id) // Same category
                    .Take(4) // Limit to 4 related products
                    .Select(p => new
                    {
                        id = p.Id,
                        name = p.Name,
                        category = p.Category?.Name ?? "General",
                        price = p.Price,
                        imageUrl = string.IsNullOrEmpty(p.ImageUrl) ?
                            $"https://placehold.co/220x150/f8fafc/64748b?text={Uri.EscapeDataString(p.Name?.Substring(0, Math.Min(p.Name.Length, 1)) ?? "P")}" :
                            p.ImageUrl,
                        description = p.Description ?? "Quality pharmacy product with professional standards."
                    })
                    .ToList();

                return new JsonResult(new { success = true, products = relatedProducts });
            }
            catch (Exception ex)
            {
                // Log the error (in production, use proper logging)
                Console.WriteLine($"Error fetching related products: {ex.Message}");
                return new JsonResult(new { success = false, message = "Unable to load related products" });
            }
        }

        // NEW: Handler for adding related products to cart (if you want quick add functionality)
        [HttpPost]
        public async Task<JsonResult> OnPostAddRelatedToCartAsync([FromForm] string productId, [FromForm] int quantity = 1)
        {
            try
            {
                if (string.IsNullOrEmpty(productId) || quantity < 1)
                {
                    return new JsonResult(new { success = false, message = "Invalid product or quantity." });
                }

                // Get the product to add
                var productDoc = await _dbService.GetCollection("product").Document(productId).GetSnapshotAsync();
                if (!productDoc.Exists)
                {
                    return new JsonResult(new { success = false, message = "Product not found." });
                }

                var product = productDoc.ConvertTo<Product>();

                // Get current cart from session
                var cart = HttpContext.Session?.GetObject<List<CartItem>>("Cart") ?? new List<CartItem>();

                // Check if product already exists in cart
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
                        Price = product.Price,
                        ImageUrl = product.ImageUrl
                    });
                }

                // Update session
                HttpContext.Session?.SetObject("Cart", cart);

                // Persist to database if user is authenticated
                var userId = User.FindFirst("user_id")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
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
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding related product to cart: {ex.Message}");
                return new JsonResult(new { success = false, message = "Failed to add product to cart." });
            }
        }
    }
}