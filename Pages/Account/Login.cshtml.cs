using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OneClick_WebApp.Pages;
using Microsoft.AspNetCore.Http;
using OneClick_WebApp.Models;
using OneClick_WebApp.Helpers;
using System;
using OneClick_WebApp.Services;
using Microsoft.Extensions.Logging;
using OneClick_WebApp.Models.ViewModel;
using OneClick_WebApp.Models.Enums;

namespace OneClick_WebApp.Pages.Account
{
    public class LoginModel : BasePageModel
    {
        private readonly SessionAuthService _sessionAuthService;
        private readonly FirebaseAuthService _authService;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(
            FirebaseDbService dbService,
            IConfiguration configuration,
            SessionAuthService sessionAuthService,
            FirebaseAuthService authService,
            ILogger<LoginModel> logger)
            : base(dbService, configuration)
        {
            _sessionAuthService = sessionAuthService;
            _authService = authService;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public class InputModel
        {
            [Required(ErrorMessage = "Email address is required.")]
            [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
            public string Email { get; set; }

            [DataType(DataType.Password)]
            public string Password { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            await LoadSiteSettingsAsync();

            // Redirect if already authenticated
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectToPage("/Index");
            }

            return Page();
        }

        /// <summary>
        /// Validates email and returns user type information
        /// </summary>
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> OnPostCheckUserAsync([FromBody] CheckUserRequest request)
        {
            _logger.LogInformation("CheckUser called with email: {Email}", request?.Email);

            if (request == null || string.IsNullOrEmpty(request.Email))
            {
                _logger.LogWarning("CheckUser called with null or empty email");
                return BadRequest("Email is required.");
            }

            try
            {
                var validationResult = await _sessionAuthService.ValidateEmailAsync(request.Email);

                if (!validationResult.IsValid)
                {
                    return BadRequest(validationResult.ErrorMessage);
                }

                if (validationResult.IsAvailable)
                {
                    // New user - redirect to registration
                    var registerUrl = $"/Account/Register?email={Uri.EscapeDataString(request.Email)}";
                    return new OkObjectResult(new
                    {
                        userType = "new_user",
                        redirectUrl = registerUrl,
                        message = "Let's create your account!"
                    });
                }

                if (validationResult.IsErpUser && !validationResult.HasPassword)
                {
                    // ERP user without password setup
                    var redirectUrl = $"/Account/SetupPassword?email={Uri.EscapeDataString(request.Email)}";
                    return new OkObjectResult(new
                    {
                        userType = "erp_no_password",
                        userName = validationResult.UserName,
                        redirectUrl = redirectUrl,
                        message = $"Welcome, {validationResult.UserName}! Please set up your password to continue."
                    });
                }

                // Existing user with password - show password field
                return new OkObjectResult(new
                {
                    userType = validationResult.IsErpUser ? "erp_with_password" : "online_customer",
                    userName = validationResult.UserName,
                    message = $"Welcome back, {validationResult.UserName}!"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking user: {Email}", request.Email);
                return StatusCode(500, "An error occurred while checking your account. Please try again.");
            }
        }

        /// <summary>
        /// SECURE AUTHENTICATION - Validates password properly using SessionAuthService
        /// </summary>
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> OnPostAuthenticateAsync([FromBody] AuthenticateRequest request)
        {
            _logger.LogInformation("Authentication attempt for email: {Email}", request?.Email);

            if (request == null || string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                return BadRequest("Email and password are required.");
            }

            try
            {
                // Use SessionAuthService for proper password verification
                var authResult = await _sessionAuthService.AuthenticateUserAsync(request.Email, request.Password);

                if (!authResult.Success)
                {
                    _logger.LogWarning("Authentication failed for {Email}: {Error}", request.Email, authResult.ErrorMessage);

                    if (authResult.RequiresSetup)
                    {
                        return new OkObjectResult(new
                        {
                            success = false,
                            requiresSetup = true,
                            redirectUrl = authResult.SetupRedirectUrl,
                            message = authResult.ErrorMessage
                        });
                    }

                    if (authResult.RequiresRegistration)
                    {
                        return new OkObjectResult(new
                        {
                            success = false,
                            requiresRegistration = true,
                            redirectUrl = authResult.RegisterRedirectUrl,
                            message = authResult.ErrorMessage
                        });
                    }

                    return BadRequest(authResult.ErrorMessage);
                }

                // Authentication successful - create session
                var userProfile = authResult.UserProfile;

                _logger.LogInformation("Authentication successful for user - Email: {Email}, Role: {Role}, IsErpUser: {IsErpUser}",
                    userProfile.Email, userProfile.UserRole, userProfile.IsErpUser);

                // Store user profile in session
                HttpContext.Session.SetObject("UserProfile", userProfile);
                HttpContext.Session.SetString("IsAuthenticated", "true");
                HttpContext.Session.SetString("LoginTime", DateTime.UtcNow.ToString("O"));

                // Load user's cart and wishlist data
                await LoadUserDataAsync(userProfile.Id);

                return new OkObjectResult(new
                {
                    success = true,
                    message = authResult.Message,
                    redirectUrl = DetermineRedirectUrl(userProfile),
                    userInfo = new
                    {
                        userProfile.Email,
                        userProfile.Name,
                        userProfile.UserRole,
                        userProfile.IsErpUser
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Authentication error for email: {Email}", request.Email);
                return StatusCode(500, "An authentication error occurred. Please try again.");
            }
        }

        /// <summary>
        /// Determines redirect URL based on user role and type
        /// </summary>
        private string DetermineRedirectUrl(UserAccount userProfile)
        {
            // Admin users go to admin dashboard
            if (userProfile.UserRole == Role.Owner || userProfile.UserRole == Role.Manager)
            {
                return "/Admin/Dashboard";
            }

            // Staff users go to staff dashboard or main page
            if (userProfile.UserRole == Role.Staff)
            {
                return "/Staff/Dashboard"; // Create this if needed, or use "/Index"
            }

            // Regular customers go to main page
            return "/Index";
        }

        /// <summary>
        /// Loads user's cart and wishlist data into session
        /// </summary>
        private async Task LoadUserDataAsync(string userId)
        {
            try
            {
                // Load cart from Firestore
                var cartDoc = _dbService.GetCollection("carts").Document(userId);
                var cartSnapshot = await cartDoc.GetSnapshotAsync();

                var cart = new List<CartItem>();

                if (cartSnapshot.Exists && cartSnapshot.TryGetValue("items", out object rawItems) && rawItems is IEnumerable<object> items)
                {
                    foreach (var itemObj in items)
                    {
                        if (itemObj is Dictionary<string, object> itemDict)
                        {
                            var cartItem = new CartItem
                            {
                                ProductId = itemDict.TryGetValue("ProductId", out var pid) ? pid?.ToString() ?? string.Empty : string.Empty,
                                ProductName = itemDict.TryGetValue("ProductName", out var pname) ? pname?.ToString() ?? string.Empty : string.Empty,
                                Quantity = itemDict.TryGetValue("Quantity", out var qtyObj) && int.TryParse(qtyObj?.ToString(), out int qty) ? qty : 1,
                                Price = itemDict.TryGetValue("Price", out var priceObj) && double.TryParse(priceObj?.ToString(), out double price) ? price : 0,
                                ImageUrl = itemDict.TryGetValue("ImageUrl", out var img) ? img?.ToString() ?? string.Empty : string.Empty
                            };

                            cart.Add(cartItem);
                        }
                    }
                }

                HttpContext.Session.SetObject("Cart", cart);

                // Load wishlist from Firestore
                var wishlistDoc = _dbService.GetCollection("wishlists").Document(userId);
                var wishlistSnapshot = await wishlistDoc.GetSnapshotAsync();

                var wishlist = new List<string>();

                if (wishlistSnapshot.Exists && wishlistSnapshot.TryGetValue("items", out object rawWishlistItems) && rawWishlistItems is IEnumerable<object> wishlistItems)
                {
                    foreach (var item in wishlistItems)
                    {
                        if (item != null)
                        {
                            wishlist.Add(item.ToString());
                        }
                    }
                }

                HttpContext.Session.SetObject("Wishlist", wishlist);

                _logger.LogInformation("Loaded cart ({CartItems} items) and wishlist ({WishlistItems} items) for user: {UserId}",
                    cart.Count, wishlist.Count, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading cart/wishlist for user {UserId}", userId);
                // Don't throw - authentication should still succeed even if cart/wishlist loading fails
                HttpContext.Session.SetObject("Cart", new List<CartItem>());
                HttpContext.Session.SetObject("Wishlist", new List<string>());
            }
        }

        // Request models
        public class CheckUserRequest
        {
            public string Email { get; set; }
        }

        public class AuthenticateRequest
        {
            public string Email { get; set; }
            public string Password { get; set; }
        }
    }
}