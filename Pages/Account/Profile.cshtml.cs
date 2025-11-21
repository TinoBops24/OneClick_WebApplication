using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OneClick_WebApp.Models;
using OneClick_WebApp.Pages;
using OneClick_WebApp.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading.Tasks;

namespace OneClick_WebApp.Pages.Account
{
    [Authorize]
    public class ProfileModel : BasePageModel
    {
        // Firebase services
        private readonly FirebaseStorageService _storageService;

        public ProfileModel(FirebaseDbService dbService, FirebaseStorageService storageService)
            : base(dbService)
        {
            _storageService = storageService;
        }

        // --- Properties for the Edit Form ---
        [BindProperty]
        public InputModel Input { get; set; }

        [BindProperty]
        [Display(Name = "New Profile Picture")]
        public IFormFile ProfilePictureUpload { get; set; }

        public string CurrentProfilePictureUrl { get; set; }

        [TempData]
        public string SuccessMessage { get; set; }
        [TempData]
        public string ErrorMessage { get; set; }

        // Input model now includes Email for display (read-only)
        public class InputModel
        {
            [Required]
            [StringLength(100, MinimumLength = 2)]
            [Display(Name = "Full Name")]
            public string Name { get; set; }

            [EmailAddress]
            [Display(Name = "Email Address")]
            public string Email { get; set; }
        }

        // --- OnGetAsync ---
        public async Task<IActionResult> OnGetAsync()
        {
            await LoadSiteSettingsAsync();

            // FIXED: Use email instead of Firebase UID for user lookup
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
            {
                return Challenge();
            }

            // FIXED: Look up user by email since UserAccount.Id = email
            UserAccount userAccount = null;

            // Try both collections to find the user
            try
            {
                // First try users collection (ERP users)
                userAccount = await _dbService.GetDocumentAsync<UserAccount>("users", userEmail);

                // If not found, try online_users collection
                if (userAccount == null)
                {
                    userAccount = await _dbService.GetDocumentAsync<UserAccount>("online_users", userEmail);
                }
            }
            catch
            {
                // Handle cases where document doesn't exist
            }

            if (userAccount == null)
            {
                ErrorMessage = "Could not find your user profile. It may have been deleted.";
                return RedirectToPage("/Index");
            }

            // Populate the form
            Input = new InputModel
            {
                Name = userAccount.Name,
                Email = userAccount.Email // from Firestore
            };

            CurrentProfilePictureUrl = userAccount.ImageUrl;

            return Page();
        }

        // --- OnPostAsync ---
        public async Task<IActionResult> OnPostAsync()
        {
            await LoadSiteSettingsAsync();

            // FIXED: Use email instead of Firebase UID
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
            {
                return Challenge();
            }

            // FIXED: Look up user by email from both collections
            UserAccount existingUser = null;
            string userCollection = null;

            try
            {
                // First try users collection (ERP users)
                existingUser = await _dbService.GetDocumentAsync<UserAccount>("users", userEmail);
                if (existingUser != null)
                {
                    userCollection = "users";
                }
                else
                {
                    // Try online_users collection
                    existingUser = await _dbService.GetDocumentAsync<UserAccount>("online_users", userEmail);
                    if (existingUser != null)
                    {
                        userCollection = "online_users";
                    }
                }
            }
            catch
            {
                // Handle lookup errors
            }

            if (existingUser == null)
            {
                ErrorMessage = "Could not find your user profile.";
                return Page();
            }

            CurrentProfilePictureUrl = existingUser?.ImageUrl;

            if (!ModelState.IsValid)
            {
                return Page();
            }

            try
            {
                // Update editable fields
                existingUser.Name = Input.Name;

                // Handle profile picture upload
                if (ProfilePictureUpload != null && ProfilePictureUpload.Length > 0)
                {
                    var fileName = $"profile_{userEmail.Replace("@", "_").Replace(".", "_")}{Path.GetExtension(ProfilePictureUpload.FileName)}";
                    using (var stream = ProfilePictureUpload.OpenReadStream())
                    {
                        var imageUrl = await _storageService.UploadFileAsync(stream, fileName, "user_profiles");
                        existingUser.ImageUrl = imageUrl;
                    }
                }

                // FIXED: Save to the correct collection using email as document ID
                await _dbService.SetDocumentAsync(userCollection, userEmail, existingUser);
                SuccessMessage = "Your profile has been updated successfully!";
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                ErrorMessage = $"An error occurred while updating your profile: {ex.Message}";
            }

            return RedirectToPage();
        }
    }
}