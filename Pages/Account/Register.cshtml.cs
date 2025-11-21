using Microsoft.AspNetCore.Mvc;
using OneClick_WebApp.Models;
using OneClick_WebApp.Models.Enums;
using OneClick_WebApp.Services;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using System;
using OneClick_WebApp.Helpers;
using FirebaseAdmin.Auth;
using OneClick_WebApp.Models.ViewModel;

namespace OneClick_WebApp.Pages.Account
{
    public class RegisterModel : BasePageModel
    {
        private readonly SessionAuthService _sessionAuthService;
        private readonly ILogger<RegisterModel> _logger;

        public RegisterModel(
            FirebaseDbService dbService,
            IConfiguration configuration,
            SessionAuthService sessionAuthService,
            ILogger<RegisterModel> logger)
            : base(dbService, configuration)
        {
            _sessionAuthService = sessionAuthService;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Full Name is required.")]
            [StringLength(100, MinimumLength = 2, ErrorMessage = "Full Name must be between 2 and 100 characters.")]
            [Display(Name = "Full Name")]
            public string Name { get; set; }

            [Required(ErrorMessage = "Email address is required.")]
            [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
            public string Email { get; set; }

            [Required(ErrorMessage = "Password is required.")]
            [StringLength(100, ErrorMessage = "The password must be at least {2} characters long.", MinimumLength = 8)]
            [DataType(DataType.Password)]
            [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$",
                ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, one number, and one special character.")]
            public string Password { get; set; }

            [Required(ErrorMessage = "Password confirmation is required.")]
            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; }
        }

        public async Task OnGetAsync()
        {
            await LoadSiteSettingsAsync();
            // Initialize Input model
            Input = new InputModel();

            // Get email from query string
            var email = Request.Query["email"].FirstOrDefault();

            // Pre-populate email if provided from login redirect
            if (!string.IsNullOrEmpty(email))
            {
                Input.Email = email;
            }
        }

        /// <summary>
        /// Validates email availability before registration
        /// </summary>
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> OnPostCheckEmailAsync([FromBody] CheckEmailRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Email))
            {
                return BadRequest("Email is required.");
            }

            try
            {
                var validationResult = await _sessionAuthService.ValidateEmailAsync(request.Email);

                if (!validationResult.IsValid)
                {
                    return BadRequest(validationResult.ErrorMessage);
                }

                if (!validationResult.IsAvailable)
                {
                    if (validationResult.IsErpUser && !validationResult.HasPassword)
                    {
                        return new OkObjectResult(new
                        {
                            isErpUser = true,
                            hasPassword = false,
                            redirectUrl = $"/Account/SetupPassword?email={request.Email}",
                            message = "Welcome back! Please set your password to continue."
                        });
                    }
                    else
                    {
                        return new OkObjectResult(new
                        {
                            isErpUser = validationResult.IsErpUser,
                            hasPassword = true,
                            message = "This email is already registered. Please use the login page."
                        });
                    }
                }

                // Email is available for registration
                return new OkObjectResult(new
                {
                    isErpUser = false,
                    canRegister = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking email: {Email}", request.Email);
                return StatusCode(500, "An error occurred while checking your email. Please try again.");
            }
        }

        /// <summary>
        /// Creates new online customer account with session
        /// </summary>
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> OnPostCreateAccountAsync([FromBody] CreateAccountRequest request)
        {
            if (request == null ||
                string.IsNullOrEmpty(request.Email) ||
                string.IsNullOrEmpty(request.Name) ||
                string.IsNullOrEmpty(request.Password))
            {
                return BadRequest("All fields are required.");
            }

            if (!new EmailAddressAttribute().IsValid(request.Email))
            {
                return BadRequest("Invalid email format.");
            }

            try
            {
                _logger.LogInformation("Creating new account for email: {Email}", request.Email);

                // Double-check email availability
                var validationResult = await _sessionAuthService.ValidateEmailAsync(request.Email);
                if (!validationResult.IsAvailable)
                {
                    return BadRequest("This email is already registered.");
                }

                // Create Firebase Authentication user
                var userArgs = new UserRecordArgs
                {
                    Email = request.Email,
                    Password = request.Password,
                    DisplayName = request.Name,
                    EmailVerified = false,
                    Disabled = false
                };

                var userRecord = await FirebaseAuth.DefaultInstance.CreateUserAsync(userArgs);
                _logger.LogInformation("Created Firebase user with UID: {Uid}", userRecord.Uid);

                // Create user profile in online_users collection
                var userProfile = new UserAccount
                {
                    Id = request.Email, // Document ID is email
                    FirebaseUid = userRecord.Uid,
                    Email = request.Email,
                    Name = request.Name,
                    UserRole = Role.Customer,
                    IsErpUser = false,
                    Disabled = false,
                    BranchAccess = new Dictionary<string, bool>()
                };

                await _dbService.SaveOnlineUserAsync(userProfile);
                _logger.LogInformation("Created user profile for: {Email}", request.Email);

                // Create session for the new user
                HttpContext.Session.SetObject("UserProfile", userProfile);
                HttpContext.Session.SetString("IsAuthenticated", "true");
                HttpContext.Session.SetString("LoginTime", DateTime.UtcNow.ToString("O"));

                // Initialize empty cart and wishlist
                HttpContext.Session.SetObject("Cart", new List<CartItem>());
                HttpContext.Session.SetObject("Wishlist", new List<string>());

                return new OkObjectResult(new
                {
                    success = true,
                    message = $"Welcome to our store, {request.Name}!",
                    redirectUrl = "/Index"
                });
            }
            catch (FirebaseAuthException ex)
            {
                _logger.LogError(ex, "Firebase error creating account for: {Email}", request.Email);
                return BadRequest("Failed to create account. Please try again.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating account for: {Email}", request.Email);
                return StatusCode(500, "An unexpected error occurred. Please try again.");
            }
        }

        public class CheckEmailRequest
        {
            public string Email { get; set; }
        }

        public class CreateAccountRequest
        {
            public string Email { get; set; }
            public string Name { get; set; }
            public string Password { get; set; }
        }
    }
}