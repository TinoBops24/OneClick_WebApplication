using FirebaseAdmin.Auth;
using OneClick_WebApp.Models;
using OneClick_WebApp.Models.Enums;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FirebaseAdmin;

namespace OneClick_WebApp.Services
{
    public class FirebaseAuthService
    {
        private readonly FirebaseDbService _dbService;
        private readonly FirestoreDb _firestore;
        private readonly ILogger<FirebaseAuthService> _logger;

        public FirebaseAuthService(FirebaseDbService dbService, IConfiguration configuration, ILogger<FirebaseAuthService> logger)
        {
            _dbService = dbService;
            _firestore = FirestoreDb.Create(configuration["Firebase:ProjectId"]);
            _logger = logger;
        }

        /// <summary>
        /// Checks if an email exists in the ERP users collection
        /// </summary>
        public async Task<ErpUser?> GetErpUserByEmailAsync(string email)
        {
            try
            {
                var query = _firestore.Collection("users").WhereEqualTo("Email", email);
                var snapshot = await query.GetSnapshotAsync();

                if (snapshot.Documents.Count > 0)
                {
                    var doc = snapshot.Documents.First();
                    var erpUser = ExtractErpUserFromDocument(doc);
                    return erpUser;
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking ERP user for email: {Email}", email);
                return null;
            }
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

        /// <summary>
        /// Registers a new online customer (not ERP user)
        /// </summary>
        /// <summary>
        /// Registers a new online customer (not ERP user).
        /// The document ID will now be the user's email address.
        /// </summary>
        public async Task<UserRecord> RegisterOnlineCustomerAsync(string email, string password, string displayName)
        {
            var userArgs = new UserRecordArgs
            {
                Email = email,
                Password = password,
                DisplayName = displayName,
                EmailVerified = false,
                Disabled = false
            };

            // Create user in Firebase Authentication
            UserRecord userRecord = await FirebaseAuth.DefaultInstance.CreateUserAsync(userArgs);

            // Create customer profile in online_users collection, consistent with your UserAccount model
            var onlineUser = new UserAccount
            {
                // The document ID property is now explicitly set to the user's email.
                Id = userRecord.Email,

                // We still store the immutable Firebase UID inside the document for reliable linking.
                FirebaseUid = userRecord.Uid,

                Email = userRecord.Email,
                Name = userRecord.DisplayName,
                UserRole = Role.Customer, // This is the correct default for new online sign-ups
                IsErpUser = false,
                // Other properties like Disabled, BranchAccess will use their default values from the model.
            };

            // REMOVED: The call to onlineUser.EnsureIdConsistency() is no longer needed.

            // Use the user's email as the document ID when saving to Firestore.
            await _dbService.SetDocumentAsync("online_users", onlineUser.Email, onlineUser);

            _logger.LogInformation("Successfully created online user document with ID (email): {UserEmail}", onlineUser.Email);

            return userRecord;
        }

        /// <summary>
        /// Creates a new Firebase user for ERP user (when no existing Firebase account exists)
        /// </summary>
        public async Task<UserRecord> CreateFirebaseUserForErpAsync(string email, string password, ErpUser erpUser)
        {
            _logger.LogInformation("Creating new Firebase user for ERP user: {Email}", email);

            var userArgs = new UserRecordArgs
            {
                Email = email,
                Password = password,
                DisplayName = erpUser.Name,
                EmailVerified = true, // ERP users are pre-verified
                Disabled = false
            };

            // Create Firebase Auth user
            UserRecord userRecord = await FirebaseAuth.DefaultInstance.CreateUserAsync(userArgs);
            _logger.LogInformation("Created Firebase user with UID: {Uid}", userRecord.Uid);

            // Update ERP user document with Firebase UID
            var erpUserDoc = _firestore.Collection("users").Document(erpUser.Id);
            await erpUserDoc.UpdateAsync("FirebaseUid", userRecord.Uid);
            _logger.LogInformation("Updated ERP user document with Firebase UID");

            return userRecord;
        }

        /// <summary>
        /// Links existing Firebase user with ERP record and UPDATES password
        /// </summary>
        public async Task<LinkResult> LinkExistingFirebaseUserWithErpAsync(string email, string password, ErpUser erpUser)
        {
            try
            {
                _logger.LogInformation("Attempting to link existing Firebase user for: {Email}", email);

                // Get existing Firebase user by email
                UserRecord existingUser = null;
                try
                {
                    existingUser = await FirebaseAuth.DefaultInstance.GetUserByEmailAsync(email);
                    _logger.LogInformation("Found existing Firebase user with UID: {Uid}", existingUser.Uid);
                }
                catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.UserNotFound)
                {
                    _logger.LogInformation("No existing Firebase user found for email: {Email}", email);
                    return new LinkResult { Success = false, RequiresNewUser = true };
                }

                if (existingUser != null)
                {
                    //  Update the password for the existing Firebase user
                    if (!string.IsNullOrEmpty(password))
                    {
                        try
                        {
                            var updateArgs = new UserRecordArgs()
                            {
                                Uid = existingUser.Uid,
                                Password = password,
                                DisplayName = erpUser.Name, // Also update display name
                                EmailVerified = true // Ensure email is verified
                            };

                            await FirebaseAuth.DefaultInstance.UpdateUserAsync(updateArgs);
                            _logger.LogInformation("Successfully updated password for Firebase user: {Uid}", existingUser.Uid);
                        }
                        catch (Exception pwEx)
                        {
                            _logger.LogError(pwEx, "Failed to update password for Firebase user: {Uid}", existingUser.Uid);
                            return new LinkResult
                            {
                                Success = false,
                                ErrorMessage = "Failed to update password. Please try again or use password reset."
                            };
                        }
                    }

                    // Update ERP user document with existing Firebase UID
                    var erpUserDoc = _firestore.Collection("users").Document(erpUser.Id);
                    await erpUserDoc.UpdateAsync("FirebaseUid", existingUser.Uid);
                    _logger.LogInformation("Successfully linked Firebase UID to ERP user document");

                    return new LinkResult
                    {
                        Success = true,
                        FirebaseUid = existingUser.Uid,
                        PasswordUpdated = !string.IsNullOrEmpty(password)
                    };
                }

                return new LinkResult { Success = false, RequiresNewUser = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error linking existing Firebase user");
                return new LinkResult
                {
                    Success = false,
                    ErrorMessage = "An error occurred while linking your account. Please try again."
                };
            }
        }

        /// <summary>
        /// Gets user data for authentication (checks both ERP and online users)
        /// </summary>
        public async Task<AuthenticatedUser?> GetAuthenticatedUserAsync(string firebaseUid)
        {
            // 1) First: ERP users by FirebaseUid (unchanged)
            var erpQuery = _firestore.Collection("users").WhereEqualTo("FirebaseUid", firebaseUid);
            var erpSnapshot = await erpQuery.GetSnapshotAsync();

            if (erpSnapshot.Documents.Count > 0)
            {
                var erpDoc = erpSnapshot.Documents.First();
                var erpUser = ExtractErpUserFromDocument(erpDoc);

                return new AuthenticatedUser
                {
                    Id = erpUser.Id,
                    FirebaseUid = firebaseUid,
                    Email = erpUser.Email,
                    Name = erpUser.Name,
                    UserRole = MapErpRoleToEnum(erpUser.UserRole),
                    IsErpUser = true,
                    BranchAccess = erpUser.BranchAccess,
                    Disabled = erpUser.Disabled
                };
            }

            // 2) Then: Online users — QUERY by FirebaseUid field instead of using doc id
            //    because your RegisterOnlineCustomerAsync now uses Email as the document id.
            var onlineQuery = _firestore.Collection("online_users").WhereEqualTo("FirebaseUid", firebaseUid);
            var onlineSnap = await onlineQuery.GetSnapshotAsync();

            if (onlineSnap.Documents.Count > 0)
            {
                var doc = onlineSnap.Documents.First();
                var onlineUser = doc.ConvertTo<UserAccount>();

              
                onlineUser.Id ??= doc.Id; 
                onlineUser.FirebaseUid ??= firebaseUid;
                onlineUser.UserRole = onlineUser.UserRole == 0 ? Role.Customer : onlineUser.UserRole;

                return new AuthenticatedUser
                {
                    Id = onlineUser.Id,
                    FirebaseUid = firebaseUid,
                    Email = onlineUser.Email,
                    Name = onlineUser.Name,
                    UserRole = onlineUser.UserRole,
                    IsErpUser = false,
                    BranchAccess = onlineUser.BranchAccess,
                    Disabled = onlineUser.Disabled
                };
            }

           
            var legacyDoc = await _firestore.Collection("online_users").Document(firebaseUid).GetSnapshotAsync();
            if (legacyDoc.Exists)
            {
                var legacyUser = legacyDoc.ConvertTo<UserAccount>();
                legacyUser.Id ??= legacyDoc.Id;
                legacyUser.FirebaseUid ??= firebaseUid;
                legacyUser.UserRole = legacyUser.UserRole == 0 ? Role.Customer : legacyUser.UserRole;

                return new AuthenticatedUser
                {
                    Id = legacyUser.Id,
                    FirebaseUid = firebaseUid,
                    Email = legacyUser.Email,
                    Name = legacyUser.Name,
                    UserRole = legacyUser.UserRole,
                    IsErpUser = false,
                    BranchAccess = legacyUser.BranchAccess,
                    Disabled = legacyUser.Disabled
                };
            }

            return null;
        }


        /// <summary>
        /// Extracts ERP user data from Firestore document with proper field mapping
        /// </summary>
        private ErpUser ExtractErpUserFromDocument(DocumentSnapshot doc)
        {
            var data = doc.ToDictionary();
            
            _logger.LogDebug("Extracting ERP user from document {DocumentId}. Available fields: {Fields}", 
                doc.Id, string.Join(", ", data.Keys));

            return new ErpUser
            {
                Id = data.TryGetValue("ID", out var id) ? id?.ToString() : doc.Id,
                Email = data.TryGetValue("Email", out var email) ? email?.ToString() : "",
                
                // Try multiple field name variations for Name
                Name = ExtractNameField(data),
                
                UserRole = data.TryGetValue("UserRole", out var role) && int.TryParse(role?.ToString(), out var roleInt) ? roleInt : 0,
                Disabled = data.TryGetValue("Disabled", out var disabled) && bool.TryParse(disabled?.ToString(), out var disabledBool) ? disabledBool : false,
                Code = data.TryGetValue("Code", out var code) ? code?.ToString() : "",
                FirebaseUid = data.TryGetValue("FirebaseUid", out var uid) ? uid?.ToString() : null,
                BranchAccess = ExtractBranchAccess(data)
            };
        }

        /// <summary>
        /// Extracts name field from document data with fallback options
        /// </summary>
        private string ExtractNameField(Dictionary<string, object> data)
        {
            // Try different possible field names for Name
            string[] nameFields = { "Name", "name", "DisplayName", "displayName", "FullName", "fullName" };
            
            foreach (var fieldName in nameFields)
            {
                if (data.TryGetValue(fieldName, out var nameValue) && !string.IsNullOrWhiteSpace(nameValue?.ToString()))
                {
                    _logger.LogDebug("Found name in field: {FieldName} = {Value}", fieldName, nameValue);
                    return nameValue.ToString();
                }
            }
            
            // If no name field found, try to construct from other fields
            if (data.TryGetValue("Email", out var emailValue))
            {
                var email = emailValue?.ToString();
                if (!string.IsNullOrEmpty(email) && email.Contains("@"))
                {
                    // Use part before @ as display name
                    return email.Split('@')[0];
                }
            }
            
            _logger.LogWarning("No name field found in document. Available fields: {Fields}", 
                string.Join(", ", data.Keys));
            
            return "Unknown User";
        }

        /// <summary>
        /// Maps ERP numeric roles to Role enum
        /// </summary>
        private Role MapErpRoleToEnum(int erpRole)
        {
            var mappedRole = erpRole switch
            {
                7 => Role.Owner,   // Owner1 (from your ERP database)
                8 => Role.Owner,   // Owner2 (from your ERP database)  
                5 => Role.Manager, // Manager role in ERP
                3 => Role.Staff,   // Staff role in ERP
                _ => Role.Customer // Default fallback for unknown roles
            };
            
            _logger.LogDebug("Mapped ERP role {ErpRole} to {MappedRole}", erpRole, mappedRole);
            return mappedRole;
        }

        /// <summary>
        /// Generates a password reset link for the given email
        /// </summary>
        public async Task<string> GetPasswordResetLinkAsync(string email)
        {
            return await FirebaseAuth.DefaultInstance.GeneratePasswordResetLinkAsync(email);
        }
    }

    // Result class for linking operations
    public class LinkResult
    {
        public bool Success { get; set; }
        public string FirebaseUid { get; set; }
        public bool PasswordUpdated { get; set; }
        public bool RequiresNewUser { get; set; }
        public string ErrorMessage { get; set; }
    }

    // Simplified ErpUser class without Firestore attributes
    public class ErpUser
    {
        public string Id { get; set; }
        public string Email { get; set; }
        public string Name { get; set; }
        public int UserRole { get; set; }
        public Dictionary<string, bool> BranchAccess { get; set; } = new();
        public bool Disabled { get; set; }
        public string Code { get; set; }
        public string? FirebaseUid { get; set; }
    }

    public class AuthenticatedUser
    {
        public string Id { get; set; }
        public string FirebaseUid { get; set; }
        public string Email { get; set; }
        public string Name { get; set; }
        public Role UserRole { get; set; }
        public bool IsErpUser { get; set; }
        public Dictionary<string, bool>? BranchAccess { get; set; }
        public bool Disabled { get; set; }
    }
}