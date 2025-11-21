using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OneClick_WebApp.Models;
using OneClick_WebApp.Models.Enums;
using OneClick_WebApp.Pages;
using OneClick_WebApp.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OneClick_WebApp.Pages.Admin
{
    [Authorize(Policy = "AdminOnly")]
    public class UserDetailsModel : BasePageModel
    {
        private readonly ILogger<UserDetailsModel> _logger;

        public UserDetailsModel(FirebaseDbService dbService, ILogger<UserDetailsModel> logger) : base(dbService)
        {
            _logger = logger;
        }

        [BindProperty(SupportsGet = true)]
        public string UserId { get; set; }

        public UserAccount User { get; set; }
        public List<POSTransaction> UserOrders { get; set; } = new();
        public List<WishlistItem> UserWishlist { get; set; } = new();

        [TempData]
        public string SuccessMessage { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            await LoadSiteSettingsAsync();

            if (string.IsNullOrEmpty(UserId))
            {
                return RedirectToPage("/Admin/Users");
            }

            await LoadUserDetailsAsync();

            if (User == null)
            {
                ErrorMessage = "User not found.";
                return RedirectToPage("/Admin/Users");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostToggleStatusAsync(bool disable)
        {
            if (string.IsNullOrEmpty(UserId))
            {
                return RedirectToPage("/Admin/Users");
            }

            try
            {
                var user = await _dbService.GetUserByIdAsync(UserId);
                if (user != null)
                {
                    user.Disabled = disable;
                    await _dbService.SaveUserAccountAsync(user);

                    SuccessMessage = $"User {(disable ? "disabled" : "enabled")} successfully.";
                    _logger.LogInformation("User {UserId} status changed to {Status}", UserId, disable ? "disabled" : "enabled");
                }
                else
                {
                    ErrorMessage = "User not found.";
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to update user status for {UserId}", UserId);
                ErrorMessage = "Failed to update user status. Please try again.";
            }

            return RedirectToPage(new { UserId });
        }

        public async Task<IActionResult> OnPostChangeRoleAsync(Role newRole)
        {
            if (string.IsNullOrEmpty(UserId))
            {
                return RedirectToPage("/Admin/Users");
            }

            try
            {
                var user = await _dbService.GetUserByIdAsync(UserId);
                if (user != null)
                {
                    user.UserRole = newRole;
                    await _dbService.SaveUserAccountAsync(user);

                    SuccessMessage = $"User role updated to {newRole}.";
                    _logger.LogInformation("User {UserId} role changed to {NewRole}", UserId, newRole);
                }
                else
                {
                    ErrorMessage = "User not found.";
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to update user role for {UserId}", UserId);
                ErrorMessage = "Failed to update user role. Please try again.";
            }

            return RedirectToPage(new { UserId });
        }

        private async Task LoadUserDetailsAsync()
        {
            try
            {
                User = await _dbService.GetUserByIdAsync(UserId);

                if (User != null)
                {
                    // Load user's orders
                    UserOrders = await _dbService.GetUserOrdersAsync(UserId);

                    // Load user's wishlist
                    UserWishlist = await _dbService.GetWishlistItemsAsync(UserId);
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to load user details for {UserId}", UserId);
                User = null;
            }
        }
    }
}