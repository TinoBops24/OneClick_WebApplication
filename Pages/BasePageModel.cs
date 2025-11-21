using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OneClick_WebApp.Models;
using OneClick_WebApp.Services;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Security.Claims;
using OneClick_WebApp.Helpers;
using OneClick_WebApp.Models.ViewModel;
using System;

namespace OneClick_WebApp.Pages
{
    public class BasePageModel : PageModel
    {
        protected readonly FirebaseDbService _dbService;
        protected readonly IConfiguration _configuration;
        protected readonly CacheManagerService _cacheManager;

        public BasePageModel(FirebaseDbService dbService, IConfiguration configuration, CacheManagerService cacheManager)
        {
            _dbService = dbService;
            _configuration = configuration;
            _cacheManager = cacheManager;
        }

        public BasePageModel(FirebaseDbService dbService, IConfiguration configuration)
        {
            _dbService = dbService;
            _configuration = configuration;
            _cacheManager = null;
        }

        public BasePageModel(FirebaseDbService dbService)
        {
            _dbService = dbService;
            _configuration = null;
            _cacheManager = null;
        }

        public IConfiguration Configuration => _configuration;

        public string InitialCartJson { get; private set; } = "[]";
        public string InitialWishlistJson { get; private set; } = "[]";

        public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            if (HttpContext.Request.Method == "GET")
            {
                // Load essential site settings on every page request
                await LoadSiteSettingsAsync();

                // Load user-specific data only if the user is authenticated
                if (User?.Identity?.IsAuthenticated ?? false)
                {
                    await LoadCartIntoSessionAsync();
                    await LoadWishlistIntoSessionAsync();
                }

                // Loaded data as JSON for the script in Layout
                InitialCartJson = HttpContext.Session.GetString("Cart") ?? "[]";
                InitialWishlistJson = HttpContext.Session.GetString("Wishlist") ?? "[]";
            }

            // Continue to execute the specific page's own handler
            await next.Invoke();
        }

        public async Task LoadSiteSettingsAsync()
        {
            // Use cached site settings if CacheManager is available
            Branch config;

            if (_cacheManager != null)
            {
                config = await _cacheManager.GetSiteSettingsAsync();
            }
            else
            {
                // Fallback to direct database call if cache manager not injected
                config = await _dbService.GetBranchConfigurationAsync();
            }

            if (config != null)
            {
                ViewData["CompanyName"] = config.CompanyName;
                ViewData["LogoUrl"] = config.LogoUrl;
                ViewData["SocialMedia_LinkedIn"] = config.SocialMedia_LinkedIn;
            }
        }

        public async Task LoadWishlistIntoSessionAsync()
        {
            var userId = User.FindFirst("user_id")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                var wishlistRef = _dbService.GetCollection("wishlists").Document(userId);
                var wishlistSnapshot = await wishlistRef.GetSnapshotAsync();
                List<string> productIds = new();
                if (wishlistSnapshot.Exists && wishlistSnapshot.ContainsField("ProductIds"))
                {
                    productIds = wishlistSnapshot.GetValue<List<string>>("ProductIds");
                }
                HttpContext.Session.SetString("Wishlist", JsonSerializer.Serialize(productIds));
            }
            else
            {
                // Session is cleared for guest users
                HttpContext.Session.SetString("Wishlist", JsonSerializer.Serialize(new List<string>()));
            }
        }

        protected string GetUserId()
        {
            // Try session-based user profile first
            var userProfile = HttpContext.Session.GetObject<UserAccount>("UserProfile");
            if (userProfile != null)
            {
                return userProfile.FirebaseUid ?? userProfile.Id;
            }

            // Fallback to claims
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId)) return userId;

            userId = User.FindFirst("user_id")?.Value;
            if (!string.IsNullOrEmpty(userId)) return userId;

            return null;
        }

        public async Task LoadCartIntoSessionAsync()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                HttpContext.Session.SetObject("Cart", new List<CartItem>());
                return;
            }
            var cartRef = _dbService.GetCollection("carts").Document(userId);
            var cartSnapshot = await cartRef.GetSnapshotAsync();
            List<CartItem> dbCart = new List<CartItem>();
            if (cartSnapshot.Exists && cartSnapshot.ContainsField("items"))
            {
                var itemsFromDb = cartSnapshot.GetValue<List<Dictionary<string, object>>>("items");
                foreach (var itemDict in itemsFromDb)
                {
                    var cartItem = new CartItem
                    {
                        ProductId = itemDict.GetValueOrDefault("ProductId")?.ToString(),
                        ProductName = itemDict.GetValueOrDefault("ProductName")?.ToString(),
                        Quantity = Convert.ToInt32(itemDict.GetValueOrDefault("Quantity", 0)),
                        Price = Convert.ToDouble(itemDict.GetValueOrDefault("Price", 0.0)),
                        ImageUrl = itemDict.GetValueOrDefault("ImageUrl")?.ToString()
                    };
                    if (!string.IsNullOrEmpty(cartItem.ProductId))
                    {
                        dbCart.Add(cartItem);
                    }
                }
            }
            HttpContext.Session.SetObject("Cart", dbCart);
        }
    }
}