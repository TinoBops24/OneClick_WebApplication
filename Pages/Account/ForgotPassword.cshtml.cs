using Microsoft.AspNetCore.Mvc;
using OneClick_WebApp.Services;
using System.ComponentModel.DataAnnotations;

namespace OneClick_WebApp.Pages.Account
{
    public class ForgotPasswordModel : BasePageModel
    {
        private readonly FirebaseAuthService _authService;

        public ForgotPasswordModel(FirebaseAuthService authService, FirebaseDbService dbService) : base(dbService)
        {
            _authService = authService;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        [TempData]
        public string Message { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }
        }

        public async Task OnGetAsync()
        {
            await LoadSiteSettingsAsync();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            await LoadSiteSettingsAsync();
            if (ModelState.IsValid)
            {
                try
                {
                    // We use our server-side service to generate the link.
                    // Firebase will handle sending the email.
                    await _authService.GetPasswordResetLinkAsync(Input.Email);
                    Message = "If an account exists with this email, a password reset link has been sent. Please check your inbox.";
                }
                catch (Exception ex)
                {
                    // Don't reveal if the user does not exist.
                    Message = "If an account exists with this email, a password reset link has been sent. Please check your inbox.";
                    // Log the actual error for debugging.
                    Console.WriteLine(ex.Message);
                }
                return Page();
            }
            return Page();
        }
    }
}