using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OneClick_WebApp.Services;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace OneClick_WebApp.Pages.Account
{
    public class SetupPasswordModel : BasePageModel
    {
        private readonly FirebaseAuthService _authService;
        private readonly ILogger<SetupPasswordModel> _logger;

        public SetupPasswordModel(
            FirebaseDbService dbService,
            IConfiguration configuration,
            FirebaseAuthService authService,
            ILogger<SetupPasswordModel> logger)
            : base(dbService, configuration)
        {
            _authService = authService;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }

            [Required]
            [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long.")]
            [DataType(DataType.Password)]
            [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$",
                ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, one number, and one special character.")]
            public string Password { get; set; }

            [Required]
            [DataType(DataType.Password)]
            [Compare("Password", ErrorMessage = "Passwords do not match.")]
            public string ConfirmPassword { get; set; }

            public string ErpUserId { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(string email)
        {
            await LoadSiteSettingsAsync();

            if (string.IsNullOrEmpty(email))
            {
                return RedirectToPage("./Login");
            }

            // Verify this email exists in ERP users
            var erpUser = await _authService.GetErpUserByEmailAsync(email);
            if (erpUser == null)
            {
                TempData["ErrorMessage"] = "No account found with this email address.";
                return RedirectToPage("./Login");
            }

            // Check if already linked to Firebase
            if (!string.IsNullOrEmpty(erpUser.FirebaseUid))
            {
                TempData["ErrorMessage"] = "This account already has a password set. Please use the login page.";
                return RedirectToPage("./Login");
            }

            Input.Email = email;
            Input.ErpUserId = erpUser.Id;

            return Page();
        }

        public class LinkErpUserRequest
        {
            public string Email { get; set; }
            public string Password { get; set; }
            public string ErpUserId { get; set; }
        }

        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> OnPostLinkErpUserAsync([FromBody] LinkErpUserRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Email) ||
                string.IsNullOrEmpty(request.Password) || string.IsNullOrEmpty(request.ErpUserId))
            {
                _logger.LogWarning("Invalid request data for LinkErpUser");
                return BadRequest("Invalid request data.");
            }

            try
            {
                _logger.LogInformation("Starting ERP user linking for: {Email}", request.Email);

                // Get ERP user details
                var erpUser = await _authService.GetErpUserByEmailAsync(request.Email);
                if (erpUser == null || erpUser.Id != request.ErpUserId)
                {
                    _logger.LogWarning("ERP user not found or ID mismatch for: {Email}", request.Email);
                    return BadRequest("Invalid user data.");
                }

                // Check if already linked
                if (!string.IsNullOrEmpty(erpUser.FirebaseUid))
                {
                    _logger.LogWarning("ERP user already linked: {Email}", request.Email);
                    return BadRequest("This account is already linked.");
                }

                // Try to link with existing Firebase user first (with password update)
                var linkResult = await _authService.LinkExistingFirebaseUserWithErpAsync(
                    request.Email,
                    request.Password,
                    erpUser);

                if (linkResult.Success)
                {
                    _logger.LogInformation("Successfully linked existing Firebase user for: {Email}, Password Updated: {PasswordUpdated}",
                        request.Email, linkResult.PasswordUpdated);

                    return new OkObjectResult(new
                    {
                        success = true,
                        message = linkResult.PasswordUpdated
                            ? "Your account has been linked and password updated successfully."
                            : "Your account has been linked successfully.",
                        canProceedToLogin = true
                    });
                }
                else if (linkResult.RequiresNewUser)
                {
                    // No existing Firebase user, create new one
                    _logger.LogInformation("Creating new Firebase user for ERP user: {Email}", request.Email);

                    await _authService.CreateFirebaseUserForErpAsync(request.Email, request.Password, erpUser);

                    return new OkObjectResult(new
                    {
                        success = true,
                        message = "Account created and linked successfully.",
                        canProceedToLogin = true
                    });
                }
                else
                {
                    // Linking failed
                    _logger.LogError("Failed to link Firebase user: {Email}, Error: {Error}",
                        request.Email, linkResult.ErrorMessage);

                    return BadRequest(linkResult.ErrorMessage ?? "Failed to link account. Please try again.");
                }
            }
            catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.EmailAlreadyExists)
            {
                // This shouldn't happen if our linking logic is correct, but handle it anyway
                _logger.LogWarning("Email already exists when trying to create new user: {Email}", request.Email);

                // Try linking again with password update
                var erpUser = await _authService.GetErpUserByEmailAsync(request.Email);
                var linkResult = await _authService.LinkExistingFirebaseUserWithErpAsync(
                    request.Email,
                    request.Password,
                    erpUser);

                if (linkResult.Success)
                {
                    return new OkObjectResult(new
                    {
                        success = true,
                        message = "Your existing account has been linked successfully.",
                        canProceedToLogin = true
                    });
                }
                else
                {
                    return BadRequest("Your email already has an account. Please use the login page or reset your password.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error linking ERP user: {Email}", request.Email);
                return BadRequest("An unexpected error occurred. Please try again.");
            }
        }
    }
}