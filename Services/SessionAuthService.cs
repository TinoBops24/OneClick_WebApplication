using FirebaseAdmin.Auth;
using Microsoft.Extensions.Logging;
using OneClick_WebApp.Models;
using OneClick_WebApp.Models.Enums;
using OneClick_WebApp.Services;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace OneClick_WebApp.Services
{
    public class SessionAuthService
    {
        private readonly FirebaseDbService _dbService;
        private readonly FirebaseAuthService _authService;
        private readonly ILogger<SessionAuthService> _logger;

        public SessionAuthService(
            FirebaseDbService dbService,
            FirebaseAuthService authService,
            ILogger<SessionAuthService> logger)
        {
            _dbService = dbService;
            _authService = authService;
            _logger = logger;
        }

        /// <summary>
        /// Verifies user credentials and returns complete user profile
        /// </summary>
        public async Task<AuthenticationResult> AuthenticateUserAsync(string email, string password)
        {
            try
            {
                _logger.LogInformation("Attempting authentication for email: {Email}", email);

                // Step 1: Check if user exists in ERP or online collections
                var erpUser = await _authService.GetErpUserByEmailAsync(email);
                UserAccount userProfile = null;
                bool isErpUser = false;

                if (erpUser != null)
                {
                    // ERP user found
                    isErpUser = true;

                    // Check if ERP user has Firebase account linked
                    if (string.IsNullOrEmpty(erpUser.FirebaseUid))
                    {
                        return new AuthenticationResult
                        {
                            Success = false,
                            ErrorMessage = "Please complete your account setup first.",
                            RequiresSetup = true,
                            SetupRedirectUrl = $"/Account/SetupPassword?email={Uri.EscapeDataString(email)}"
                        };
                    }

                    // Convert ERP user to UserAccount format
                    userProfile = new UserAccount
                    {
                        Id = erpUser.Id,
                        FirebaseUid = erpUser.FirebaseUid,
                        Email = erpUser.Email,
                        Name = erpUser.Name,
                        UserRole = MapErpRoleToEnum(erpUser.UserRole),
                        IsErpUser = true,
                        BranchAccess = erpUser.BranchAccess,
                        Disabled = erpUser.Disabled,
                        Code = erpUser.Code
                    };
                }
                else
                {
                    // Check online users collection
                    userProfile = await _dbService.GetOnlineUserByEmailIdAsync(email);

                    if (userProfile == null)
                    {
                        return new AuthenticationResult
                        {
                            Success = false,
                            ErrorMessage = "No account found with this email address.",
                            RequiresRegistration = true,
                            RegisterRedirectUrl = $"/Account/Register?email={Uri.EscapeDataString(email)}"
                        };
                    }

                    isErpUser = false;
                    userProfile.IsErpUser = false;
                }

                // Check if account is disabled
                if (userProfile.Disabled)
                {
                    return new AuthenticationResult
                    {
                        Success = false,
                        ErrorMessage = "Your account has been disabled. Please contact support."
                    };
                }

                // Verify password with Firebase Auth
                try
                {
                    // Get Firebase Web API key from configuration
                    var firebaseApiKey = "AIzaSyBukriN67mHi3EYKOwQ3NkaqL0lZB48d4U"; 

                    if (string.IsNullOrEmpty(firebaseApiKey) || firebaseApiKey == "YOUR_FIREBASE_WEB_API_KEY")
                    {
                        _logger.LogWarning("Firebase API key not configured - using fallback authentication");

                       
                        if (!string.IsNullOrEmpty(password) && password.Length >= 6)
                        {
                            _logger.LogInformation("Fallback authentication successful for user: {Email}", email);

                            return new AuthenticationResult
                            {
                                Success = true,
                                UserProfile = userProfile,
                                IsErpUser = isErpUser,
                                Message = $"Welcome back, {userProfile.Name ?? userProfile.Email}!"
                            };
                        }
                        else
                        {
                            return new AuthenticationResult
                            {
                                Success = false,
                                ErrorMessage = "Password is required."
                            };
                        }
                    }

                    // Use Firebase REST API for password verification
                    var signInRequest = new
                    {
                        email = email,
                        password = password,
                        returnSecureToken = true
                    };

                    using var httpClient = new HttpClient();

                    var response = await httpClient.PostAsJsonAsync(
                        $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={firebaseApiKey}",
                        signInRequest
                    );

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogWarning("Firebase authentication failed for {Email}: {Error}", email, errorContent);

                        return new AuthenticationResult
                        {
                            Success = false,
                            ErrorMessage = "Invalid email or password."
                        };
                    }

                    // Parse the JSON response properly
                    var responseContent = await response.Content.ReadAsStringAsync();
                    using var jsonDocument = JsonDocument.Parse(responseContent);
                    var root = jsonDocument.RootElement;

                    // Verify the returned email matches what we expect
                    if (root.TryGetProperty("email", out var emailElement))
                    {
                        var responseEmail = emailElement.GetString();
                        if (responseEmail != email)
                        {
                            return new AuthenticationResult
                            {
                                Success = false,
                                ErrorMessage = "Authentication failed. Please try again."
                            };
                        }
                    }

                    _logger.LogInformation("Password verification successful for user: {Email}", email);

                    return new AuthenticationResult
                    {
                        Success = true,
                        UserProfile = userProfile,
                        IsErpUser = isErpUser,
                        Message = $"Welcome back, {userProfile.Name ?? userProfile.Email}!"
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Password verification failed for user: {Email}", email);
                    return new AuthenticationResult
                    {
                        Success = false,
                        ErrorMessage = "Invalid email or password."
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Authentication error for email: {Email}", email);
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorMessage = "An error occurred during authentication. Please try again."
                };
            }
        }

        /// <summary>
        /// Gets user profile by Firebase UID for session restoration
        /// </summary>
        public async Task<UserAccount> GetUserProfileAsync(string firebaseUid)
        {
            if (string.IsNullOrEmpty(firebaseUid))
                return null;

            try
            {
                // First check ERP users
                var erpQuery = _dbService.GetCollection("users").WhereEqualTo("FirebaseUid", firebaseUid);
                var erpSnapshot = await erpQuery.GetSnapshotAsync();

                if (erpSnapshot.Documents.Count > 0)
                {
                    var erpDoc = erpSnapshot.Documents.First();
                    var data = erpDoc.ToDictionary();

                    var userProfile = new UserAccount
                    {
                        Id = data.TryGetValue("ID", out var id) ? id?.ToString() : erpDoc.Id,
                        FirebaseUid = firebaseUid,
                        Email = data.TryGetValue("Email", out var email) ? email?.ToString() : "",
                        Name = ExtractNameField(data),
                        UserRole = MapErpRoleToEnum(data.TryGetValue("UserRole", out var role) && int.TryParse(role?.ToString(), out var roleInt) ? roleInt : 0),
                        IsErpUser = true,
                        BranchAccess = ExtractBranchAccess(data),
                        Disabled = data.TryGetValue("Disabled", out var disabled) && bool.TryParse(disabled?.ToString(), out var disabledBool) ? disabledBool : false,
                        Code = data.TryGetValue("Code", out var code) ? code?.ToString() : ""
                    };

                    return userProfile;
                }

                // Then check online users
                var onlineQuery = _dbService.GetCollection("online_users").WhereEqualTo("FirebaseUid", firebaseUid);
                var onlineSnapshot = await onlineQuery.GetSnapshotAsync();

                if (onlineSnapshot.Documents.Count > 0)
                {
                    var onlineDoc = onlineSnapshot.Documents.First();
                    var userProfile = onlineDoc.ConvertTo<UserAccount>();
                    userProfile.IsErpUser = false;
                    return userProfile;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user profile for UID: {FirebaseUid}", firebaseUid);
                return null;
            }
        }

        /// <summary>
        /// Validates email format and availability
        /// </summary>
        public async Task<EmailValidationResult> ValidateEmailAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return new EmailValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Email is required."
                };
            }

            if (!IsValidEmailFormat(email))
            {
                return new EmailValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Please enter a valid email address."
                };
            }

            try
            {
                // Check if email exists in ERP users
                var erpUser = await _authService.GetErpUserByEmailAsync(email);
                if (erpUser != null)
                {
                    return new EmailValidationResult
                    {
                        IsValid = true,
                        IsErpUser = true,
                        HasPassword = !string.IsNullOrEmpty(erpUser.FirebaseUid),
                        UserName = erpUser.Name
                    };
                }

                // Check if email exists in online users
                var onlineUser = await _dbService.GetOnlineUserByEmailIdAsync(email);
                if (onlineUser != null)
                {
                    return new EmailValidationResult
                    {
                        IsValid = true,
                        IsErpUser = false,
                        HasPassword = true,
                        UserName = onlineUser.Name
                    };
                }

                // Email not found - available for registration
                return new EmailValidationResult
                {
                    IsValid = true,
                    IsAvailable = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating email: {Email}", email);
                return new EmailValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Unable to validate email. Please try again."
                };
            }
        }

        private string ExtractNameField(Dictionary<string, object> data)
        {
            string[] nameFields = { "Name", "name", "DisplayName", "displayName", "FullName", "fullName" };

            foreach (var fieldName in nameFields)
            {
                if (data.TryGetValue(fieldName, out var nameValue) && !string.IsNullOrWhiteSpace(nameValue?.ToString()))
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

            if (data.TryGetValue("BranchAccess", out var branchAccessObj) && branchAccessObj is Dictionary<string, object> branchDict)
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

        private bool IsValidEmailFormat(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }
    }

    // Result classes
    public class AuthenticationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string Message { get; set; }
        public UserAccount UserProfile { get; set; }
        public bool IsErpUser { get; set; }
        public bool RequiresSetup { get; set; }
        public string SetupRedirectUrl { get; set; }
        public bool RequiresRegistration { get; set; }
        public string RegisterRedirectUrl { get; set; }
    }

    public class EmailValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsErpUser { get; set; }
        public bool HasPassword { get; set; }
        public string UserName { get; set; }
        public bool IsAvailable { get; set; }
    }
}