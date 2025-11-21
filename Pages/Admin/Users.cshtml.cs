using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using OneClick_WebApp.Models;
using OneClick_WebApp.Models.Enums;
using OneClick_WebApp.Pages;
using OneClick_WebApp.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;

namespace OneClick_WebApp.Pages.Admin
{
    [Authorize(Policy = "AdminOnly")]
    public class UsersModel : BasePageModel
    {
        private readonly ILogger<UsersModel> _logger;
        private readonly FirebaseAuthService _authService;
        private readonly IMemoryCache _cache;

        private const string ErpUsersCacheKey = "Admin_ErpUsers";
        private const string OnlineUsersCacheKey = "Admin_OnlineUsers";

        public UsersModel(FirebaseDbService dbService, FirebaseAuthService authService,
            IMemoryCache cache, ILogger<UsersModel> logger) : base(dbService)
        {
            _logger = logger;
            _authService = authService;
            _cache = cache;
        }

        public List<UserAccount> Users { get; set; } = new();
        public int TotalCount { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

        [BindProperty(SupportsGet = true)]
        public string SearchTerm { get; set; }

        [BindProperty(SupportsGet = true)]
        public Role? RoleFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool? DisabledFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public int Page { get; set; } = 1;

        [TempData]
        public string SuccessMessage { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public async Task OnGetAsync()
        {
            await LoadSiteSettingsAsync();
            CurrentPage = Page;
            await LoadUsersAsync();
        }

        private async Task LoadUsersAsync()
        {
            try
            {
                _logger.LogInformation("Loading users with filters: search={SearchTerm}, role={RoleFilter}, disabled={DisabledFilter}",
                    SearchTerm, RoleFilter, DisabledFilter);

                var allUsers = new List<UserAccount>();

                var erpUsers = await GetCachedErpUsersAsync();
                allUsers.AddRange(erpUsers);

                var onlineUsers = await GetCachedOnlineUsersAsync();
                allUsers.AddRange(onlineUsers);

                _logger.LogInformation("Loaded {TotalUsers} users ({ErpCount} ERP, {OnlineCount} online)",
                    allUsers.Count, erpUsers.Count, onlineUsers.Count);

                var filteredUsers = allUsers.AsEnumerable();

                if (!string.IsNullOrWhiteSpace(SearchTerm))
                {
                    filteredUsers = filteredUsers.Where(u =>
                        (!string.IsNullOrEmpty(u.Name) && u.Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(u.Email) && u.Email.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(u.Code) && u.Code.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)));
                }

                if (RoleFilter.HasValue)
                {
                    filteredUsers = filteredUsers.Where(u => u.UserRole == RoleFilter.Value);
                }

                if (DisabledFilter.HasValue)
                {
                    filteredUsers = filteredUsers.Where(u => u.Disabled == DisabledFilter.Value);
                }

                TotalCount = filteredUsers.Count();

                Users = filteredUsers
                    .OrderBy(u => u.UserRole)
                    .ThenBy(u => u.Email)
                    .Skip((Page - 1) * PageSize)
                    .Take(PageSize)
                    .ToList();

                CurrentPage = Page;
                _logger.LogInformation("Displaying {UserCount} users (page {CurrentPage}/{TotalPages})",
                    Users.Count, CurrentPage, TotalPages);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "User load failed");
                ErrorMessage = "Failed to load users. Please try again.";
                Users = new List<UserAccount>();
                TotalCount = 0;
            }
        }

        private async Task<List<UserAccount>> GetCachedErpUsersAsync()
        {
            return await _cache.GetOrCreateAsync(ErpUsersCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                entry.SetPriority(CacheItemPriority.Normal);
                return await LoadErpUsersWithProperMapping();
            });
        }

        private async Task<List<UserAccount>> GetCachedOnlineUsersAsync()
        {
            return await _cache.GetOrCreateAsync(OnlineUsersCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
                entry.SetPriority(CacheItemPriority.Normal);

                try
                {
                    var onlineUsers = await _dbService.GetAllDocumentsAsync<UserAccount>("online_users");
                    foreach (var user in onlineUsers)
                    {
                        user.IsErpUser = false;
                    }
                    return onlineUsers;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Online users collection unavailable");
                    return new List<UserAccount>();
                }
            });
        }

        private async Task<List<UserAccount>> LoadErpUsersWithProperMapping()
        {
            try
            {
                var query = _dbService.GetCollection("users");
                var snapshot = await query.GetSnapshotAsync();
                var erpUsers = new List<UserAccount>();

                foreach (var doc in snapshot.Documents)
                {
                    try
                    {
                        var data = doc.ToDictionary();

                        var userAccount = new UserAccount
                        {
                            Id = data.TryGetValue("ID", out var id) ? id?.ToString() : doc.Id,
                            Email = data.TryGetValue("Email", out var email) ? email?.ToString() : "",
                            Name = ExtractNameField(data, doc.Id),
                            Code = data.TryGetValue("Code", out var code) ? code?.ToString() : "",
                            FirebaseUid = data.TryGetValue("FirebaseUid", out var uid) ? uid?.ToString() : null,
                            Disabled = data.TryGetValue("Disabled", out var disabled) &&
                                      bool.TryParse(disabled?.ToString(), out var disabledBool) && disabledBool,
                            BranchAccess = ExtractBranchAccess(data),
                            IsErpUser = true
                        };

                        if (data.TryGetValue("UserRole", out var roleValue) &&
                            int.TryParse(roleValue?.ToString(), out var numericRole))
                        {
                            userAccount.UserRole = MapErpRoleToEnum(numericRole);
                        }
                        else
                        {
                            userAccount.UserRole = Role.Customer;
                        }

                        erpUsers.Add(userAccount);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process ERP user document: {DocumentId}", doc.Id);
                        continue;
                    }
                }

                _logger.LogInformation("Loaded {ErpUserCount} ERP users", erpUsers.Count);
                return erpUsers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ERP users load failed");
                return new List<UserAccount>();
            }
        }

        private string ExtractNameField(Dictionary<string, object> data, string documentId)
        {
            string[] nameFields = { "Name", "name", "DisplayName", "displayName", "FullName", "fullName" };

            foreach (var fieldName in nameFields)
            {
                if (data.TryGetValue(fieldName, out var nameValue) &&
                    !string.IsNullOrWhiteSpace(nameValue?.ToString()))
                {
                    return nameValue.ToString();
                }
            }

            if (data.TryGetValue("Email", out var emailValue))
            {
                var email = emailValue?.ToString();
                if (!string.IsNullOrEmpty(email) && email.Contains("@"))
                {
                    return email.Split('@')[0];
                }
            }

            return "Unknown User";
        }

        private Dictionary<string, bool> ExtractBranchAccess(Dictionary<string, object> data)
        {
            var branchAccess = new Dictionary<string, bool>();

            if (data.TryGetValue("BranchAccess", out var branchAccessObj) &&
                branchAccessObj is Dictionary<string, object> branchDict)
            {
                foreach (var kvp in branchDict)
                {
                    if (bool.TryParse(kvp.Value?.ToString(), out var accessValue))
                    {
                        branchAccess[kvp.Key] = accessValue;
                    }
                }
            }

            return branchAccess;
        }

        private Role MapErpRoleToEnum(int erpRole)
        {
            return erpRole switch
            {
                7 => Role.Owner,
                8 => Role.Owner,
                5 => Role.Manager,
                3 => Role.Staff,
                _ => Role.Customer
            };
        }

        // --- TOGGLE: online customer accounts only ---
        public async Task<IActionResult> OnPostToggleUserStatusAsync(string userId, bool shouldDisable, bool isErpUser)
        {
            // Always go back to the Users page with filters/pagination preserved
            IActionResult RedirectBack() =>
                RedirectToPage("/Admin/Users", new { Page, SearchTerm, RoleFilter, DisabledFilter });

            if (string.IsNullOrWhiteSpace(userId))
            {
                ErrorMessage = "User ID required.";
                return RedirectBack();
            }

            try
            {
                // Only allow toggling *online* users from this screen
                if (isErpUser)
                {
                    ErrorMessage = "ERP users cannot be enabled/disabled from this page.";
                    return RedirectBack();
                }

                var user = await GetUserByAnyIdAsync(userId, preferOnline: true);

                if (user == null || user.IsErpUser)
                {
                    ErrorMessage = "Only online customer accounts can be enabled/disabled here.";
                    return RedirectBack();
                }

                // Enforce customer-only toggle (optional, but matches your requirement)
                if (user.UserRole != Role.Customer)
                {
                    ErrorMessage = "Only customer accounts can be enabled/disabled here.";
                    return RedirectBack();
                }

                // Log current state before change
                _logger.LogInformation("User {Email} current status: Disabled={CurrentStatus}, attempting to set to Disabled={NewStatus}",
                    user.Email, user.Disabled, shouldDisable);

                user.Disabled = shouldDisable;
                await _dbService.SaveUserAccountAsync(user);

                // Invalidate caches
                _cache.Remove(OnlineUsersCacheKey);

                string actionPerformed = shouldDisable ? "disabled" : "enabled";
                SuccessMessage = $"User '{user.Name ?? user.Email}' has been {actionPerformed}.";
                _logger.LogInformation("User {Email} status successfully changed to {Status}", user.Email, actionPerformed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "User status update failed: {UserId}", userId);
                ErrorMessage = "Failed to update user status. Please try again.";
            }

            return RedirectBack();
        }

        // DELETE stays the same, only redirect fixed
        public async Task<IActionResult> OnPostDeleteUserAsync(string userId)
        {
            IActionResult RedirectBack() =>
                RedirectToPage("/Admin/Users", new { Page, SearchTerm, RoleFilter, DisabledFilter });

            if (string.IsNullOrWhiteSpace(userId))
            {
                ErrorMessage = "User ID required.";
                return RedirectBack();
            }

            try
            {
                var currentUserEmail = User.Identity?.Name;
                var userToDelete = await GetUserByAnyIdAsync(userId, preferOnline: true);

                if (userToDelete != null)
                {
                    if (userToDelete.Email == currentUserEmail)
                    {
                        ErrorMessage = "Cannot delete your own account.";
                        return RedirectBack();
                    }

                    if (userToDelete.IsErpUser)
                    {
                        ErrorMessage = "ERP users cannot be deleted from this interface.";
                        return RedirectBack();
                    }

                    if (userToDelete.UserRole == Role.Admin)
                    {
                        var allAdmins = await _dbService.GetAllDocumentsAsync<UserAccount>("users");
                        var adminCount = allAdmins.Count(u => u.UserRole == Role.Admin && !u.Disabled);

                        if (adminCount <= 1)
                        {
                            ErrorMessage = "Cannot delete the last active admin.";
                            return RedirectBack();
                        }
                    }

                    await DeleteUserRelatedDataAsync(userToDelete.FirebaseUid);

                    string collection = userToDelete.IsErpUser ? "users" : "online_users";
                    string documentId = userToDelete.Id ?? _dbService.GenerateUserId(userToDelete.FirebaseUid);
                    await _dbService.DeleteDocumentAsync(collection, documentId);

                    _cache.Remove(OnlineUsersCacheKey);

                    SuccessMessage = $"User '{userToDelete.Name ?? userToDelete.Email}' deleted.";
                    _logger.LogInformation("User deleted: {UserId} ({Email})", userId, userToDelete.Email);
                }
                else
                {
                    ErrorMessage = "User not found.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "User deletion failed: {UserId}", userId);
                ErrorMessage = "Failed to delete user.";
            }

            return RedirectBack();
        }

        private async Task DeleteUserRelatedDataAsync(string firebaseUid)
        {
            if (string.IsNullOrWhiteSpace(firebaseUid))
                return;

            try
            {
                string wishlistId = _dbService.GenerateUserId(firebaseUid);
                await _dbService.DeleteDocumentAsync("wishlists", wishlistId);
                _logger.LogInformation("Deleted wishlist for user: {UserId}", firebaseUid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete related data: {UserId}", firebaseUid);
            }
        }

        /// <summary>
        /// Retrieves user by any ID. If preferOnline==true, check online users first to avoid
        /// accidentally operating on an ERP user with the same email/ID.
        /// </summary>
        private async Task<UserAccount> GetUserByAnyIdAsync(string userId, bool preferOnline)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return null;

            try
            {
                // First, try the users currently rendered on the page with the desired priority
                IEnumerable<UserAccount> ordered = Users;

                if (preferOnline)
                    ordered = Users.OrderBy(u => u.IsErpUser); // false (online) first
                else
                    ordered = Users.OrderByDescending(u => u.IsErpUser);

                var existingUser = ordered.FirstOrDefault(u =>
                    u.Id == userId ||
                    u.FirebaseUid == userId ||
                    u.Email == userId ||
                    u.DisplayId == userId);

                if (existingUser != null)
                    return existingUser;

                // Cached collections
                var onlineUsers = await GetCachedOnlineUsersAsync();
                var onlineUser = onlineUsers.FirstOrDefault(u =>
                    u.Id == userId ||
                    u.FirebaseUid == userId ||
                    u.Email == userId ||
                    u.DisplayId == userId);
                if (preferOnline && onlineUser != null)
                {
                    onlineUser.IsErpUser = false;
                    return onlineUser;
                }

                var erpUsers = await GetCachedErpUsersAsync();
                var erpUser = erpUsers.FirstOrDefault(u =>
                    u.Id == userId ||
                    u.FirebaseUid == userId ||
                    u.Email == userId ||
                    u.DisplayId == userId);
                if (erpUser != null)
                    return erpUser;

                // If not found in preferred order, try the other list too
                if (!preferOnline && onlineUser != null)
                {
                    onlineUser.IsErpUser = false;
                    return onlineUser;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "User lookup failed: {UserId}", userId);
                return null;
            }
        }
    }
}
