using Google.Api.Gax;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using OneClick_WebApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OneClick_WebApp.Services
{
    public class FirebaseDbService
    {
        private readonly FirestoreDb _firestoreDb;
        private readonly ILogger<FirebaseDbService> _logger;
        private const string BranchConfigCollection = "settings";
        private const string BranchConfigDocument = "main-config"; 

        public FirebaseDbService(IConfiguration configuration, ILogger<FirebaseDbService> logger)
        {
            _logger = logger;
            string projectId = configuration["Firebase:ProjectId"];
            string privateKeyFilePath = configuration["Firebase:PrivateKeyFilePath"];

            if (string.IsNullOrWhiteSpace(projectId))
                throw new InvalidOperationException("Missing Firebase:ProjectId in configuration.");
            if (string.IsNullOrWhiteSpace(privateKeyFilePath))
                throw new InvalidOperationException("Missing Firebase:PrivateKeyFilePath in configuration.");

            var credential = GoogleCredential.FromFile(privateKeyFilePath);

            _firestoreDb = new FirestoreDbBuilder
            {
                ProjectId = projectId,
                Credential = credential,
                EmulatorDetection = EmulatorDetection.None,
            }.Build();
        }

        

        public async Task<T> GetDocumentAsync<T>(string collectionName, string documentId) where T : class
        {
            DocumentReference docRef = _firestoreDb.Collection(collectionName).Document(documentId);
            DocumentSnapshot snapshot = await docRef.GetSnapshotAsync();
            return snapshot.Exists ? snapshot.ConvertTo<T>() : null;
        }

        public async Task<List<T>> GetAllDocumentsAsync<T>(string collectionName) where T : class
        {
            Query allQuery = _firestoreDb.Collection(collectionName);
            QuerySnapshot allQuerySnapshot = await allQuery.GetSnapshotAsync();
            List<T> documents = new List<T>();
            foreach (DocumentSnapshot documentSnapshot in allQuerySnapshot.Documents)
            {
                documents.Add(documentSnapshot.ConvertTo<T>());
            }
            return documents;
        }

        public async Task SetDocumentAsync<T>(string collectionName, string documentId, T data)
        {
            DocumentReference docRef = _firestoreDb.Collection(collectionName).Document(documentId);
            await docRef.SetAsync(data, SetOptions.MergeAll);
        }

        public async Task<string> AddDocumentAsync<T>(string collectionName, T data)
        {
            CollectionReference collRef = _firestoreDb.Collection(collectionName);
            DocumentReference addedDocRef = await collRef.AddAsync(data);
            return addedDocRef.Id;
        }

        public async Task<string> AddDocumentWithIdAsync<T>(string collectionName, T data, string structuredId)
        {
            DocumentReference docRef = _firestoreDb.Collection(collectionName).Document(structuredId);
            await docRef.SetAsync(data);
            return structuredId;
        }

        public async Task DeleteDocumentAsync(string collectionName, string documentId)
        {
            DocumentReference docRef = _firestoreDb.Collection(collectionName).Document(documentId);
            await docRef.DeleteAsync();
        }

        public CollectionReference GetCollection(string collectionName)
        {
            return _firestoreDb.Collection(collectionName);
        }

        // Get all products with caching support
        public async Task<List<Product>> GetAllProductsAsync()
        {
            return await GetAllDocumentsAsync<Product>("product");
        }



        public async Task<Branch> GetBranchConfigurationAsync()
        {
            try
            {
                _logger.LogInformation("GetBranchConfigurationAsync - Querying document ID: {DocumentId}", BranchConfigDocument);

                // Always use the fixed document ID
                var document = await _firestoreDb
                    .Collection(BranchConfigCollection)
                    .Document(BranchConfigDocument)
                    .GetSnapshotAsync();

                if (document.Exists)
                {
                    var branch = document.ConvertTo<Branch>();
                    _logger.LogInformation("GetBranchConfigurationAsync - Found: CompanyName={CompanyName}, LogoUrl={LogoUrl}",
                        branch.CompanyName, branch.LogoUrl);
                    return branch;
                }

                _logger.LogWarning("GetBranchConfigurationAsync - No document found for ID: {DocumentId}", BranchConfigDocument);

                // Fallback: Look for legacy documents and migrate them
                return await MigrateLegacyConfigurationAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetBranchConfigurationAsync - Error retrieving configuration");
                return null;
            }
        }

        public async Task SaveBranchConfigurationAsync(Branch config)
        {
            try
            {
                _logger.LogInformation("SaveBranchConfigurationAsync - Saving to document ID: {DocumentId}, CompanyName: {CompanyName}",
                    BranchConfigDocument, config.CompanyName);

                // Always use the fixed document ID
                await _firestoreDb
                    .Collection(BranchConfigCollection)
                    .Document(BranchConfigDocument)
                    .SetAsync(config);

                _logger.LogInformation("SaveBranchConfigurationAsync - Successfully saved to: {DocumentId}", BranchConfigDocument);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveBranchConfigurationAsync - Error saving configuration");
                throw;
            }
        }

        
        private async Task<Branch> MigrateLegacyConfigurationAsync()
        {
            try
            {
                _logger.LogInformation("MigrateLegacyConfigurationAsync - Looking for legacy configurations");

                // Get all documents in the settings collection
                var settingsCollection = await GetAllDocumentsAsync<Branch>(BranchConfigCollection);
                var legacyConfig = settingsCollection.FirstOrDefault();

                if (legacyConfig != null)
                {
                    _logger.LogInformation("MigrateLegacyConfigurationAsync - Found legacy config: {CompanyName}", legacyConfig.CompanyName);

                    // Save to the new fixed document ID
                    await SaveBranchConfigurationAsync(legacyConfig);

                    _logger.LogInformation("MigrateLegacyConfigurationAsync - Migrated to new document ID");
                    return legacyConfig;
                }

                _logger.LogWarning("MigrateLegacyConfigurationAsync - No legacy configurations found");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MigrateLegacyConfigurationAsync - Error during migration");
                return null;
            }
        }

       
        public async Task CleanupLegacyConfigurationsAsync()
        {
            try
            {
                _logger.LogInformation("CleanupLegacyConfigurationsAsync - Starting cleanup");

                var allDocs = await _firestoreDb.Collection(BranchConfigCollection).GetSnapshotAsync();
                int deletedCount = 0;

                foreach (var doc in allDocs.Documents)
                {
                    // Skip the main config document
                    if (doc.Id == BranchConfigDocument)
                        continue;

                    // Delete legacy documents
                    await doc.Reference.DeleteAsync();
                    deletedCount++;
                    _logger.LogInformation("CleanupLegacyConfigurationsAsync - Deleted legacy document: {DocumentId}", doc.Id);
                }

                _logger.LogInformation("CleanupLegacyConfigurationsAsync - Cleanup complete. Deleted {Count} documents", deletedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CleanupLegacyConfigurationsAsync - Error during cleanup");
            }
        }

       

        public async Task AddToWishlistAsync(string userId, WishlistItem item)
        {
            // Use structured user ID
            string docId = userId.StartsWith("user_") ? userId : GenerateUserId(userId);
            var docRef = _firestoreDb.Collection("wishlists").Document(docId);
            var snapshot = await docRef.GetSnapshotAsync();

            Dictionary<string, object> newItem = new()
            {
                { "ProductId", item.ProductId },
                { "ProductName", item.ProductName },
                { "ImageUrl", item.ImageUrl },
                { "Price", item.Price },
                { "AddedAt", Timestamp.GetCurrentTimestamp() }
            };

            if (snapshot.Exists)
            {
                await docRef.UpdateAsync("items", FieldValue.ArrayUnion(newItem));
            }
            else
            {
                Dictionary<string, object> wishlistData = new()
                {
                    { "userId", userId },
                    { "items", new[] { newItem } },
                    { "createdAt", Timestamp.GetCurrentTimestamp() }
                };
                await docRef.SetAsync(wishlistData);
            }
        }

        public async Task<List<WishlistItem>> GetWishlistItemsAsync(string userId)
        {
            // Use structured user ID
            string docId = userId.StartsWith("user_") ? userId : GenerateUserId(userId);
            var docRef = _firestoreDb.Collection("wishlists").Document(docId);
            var snapshot = await docRef.GetSnapshotAsync();

            if (!snapshot.Exists || !snapshot.TryGetValue("items", out List<object> rawItems))
            {
                return new List<WishlistItem>();
            }

            return rawItems
                .OfType<Dictionary<string, object>>()
                .Select(item => new WishlistItem
                {
                    ProductId = item["ProductId"]?.ToString(),
                    ProductName = item["ProductName"]?.ToString(),
                    ImageUrl = item["ImageUrl"]?.ToString(),
                    Price = item.TryGetValue("Price", out var priceObj) && double.TryParse(priceObj.ToString(), out var price)
                        ? price : 0
                })
                .ToList();
        }

        public async Task RemoveFromWishlistAsync(string userId, string productId)
        {
            // Use structured user ID
            string docId = userId.StartsWith("user_") ? userId : GenerateUserId(userId);
            var docRef = _firestoreDb.Collection("wishlists").Document(docId);

            // Use a transaction to avoid clobbering concurrent changes
            await _firestoreDb.RunTransactionAsync(async tx =>
            {
                var snapshot = await tx.GetSnapshotAsync(docRef);
                if (!snapshot.Exists || !snapshot.TryGetValue("items", out List<object> rawItems))
                    return;

                var updatedItems = rawItems
                    .OfType<Dictionary<string, object>>()
                    .Where(item => item["ProductId"]?.ToString() != productId)
                    .ToList();

                tx.Update(docRef, new Dictionary<string, object>
                {
                    ["items"] = updatedItems
                });
            });
        }

        
        /// <summary>
        /// Generates a structured document ID for users
        /// </summary>
        public string GenerateUserId(string firebaseAuthUid)
        {
            if (string.IsNullOrWhiteSpace(firebaseAuthUid))
                throw new ArgumentException("Firebase Auth UID is required");

            return $"user_{firebaseAuthUid}";
        }

        /// <summary>
        /// Generates a structured document ID for orders/transactions with daily counter
        /// </summary>
        public async Task<string> GenerateOrderIdAsync()
        {
            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            var counterDoc = $"counter_orders_{today}";

            // Use Firestore transactions for atomic counter increment
            var counterRef = _firestoreDb.Collection("counters").Document(counterDoc);

            var transactionResult = await _firestoreDb.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(counterRef);
                long nextNumber = 1;

                if (snapshot.Exists && snapshot.TryGetValue("value", out object val))
                {
                    nextNumber = Convert.ToInt64(val) + 1;
                }

                transaction.Set(counterRef, new Dictionary<string, object>
                {
                    { "value", nextNumber },
                    { "lastUpdated", Timestamp.GetCurrentTimestamp() }
                });

                return nextNumber;
            });

            return $"order_{today}_{transactionResult:D4}";
        }

        /// <summary>
        /// Generates a structured document ID for products
        /// </summary>
        public string GenerateProductId(string sku = null, string productName = null, string category = null)
        {
            if (!string.IsNullOrWhiteSpace(sku))
            {
                return $"prod_{SanitizeForDocumentId(sku)}";
            }

            if (!string.IsNullOrWhiteSpace(productName))
            {
                var prefix = !string.IsNullOrWhiteSpace(category)
                    ? $"prod_{SanitizeForDocumentId(category)}_"
                    : "prod_";
                return $"{prefix}{SanitizeForDocumentId(productName)}";
            }

            // Fallback to timestamp-based ID
            return $"prod_{DateTime.UtcNow.Ticks}";
        }

        /// <summary>
        /// Generates a structured document ID for branches
        /// </summary>
        public string GenerateBranchId(string branchName, string branchCode = null)
        {
            if (!string.IsNullOrWhiteSpace(branchCode))
            {
                return $"branch_{SanitizeForDocumentId(branchCode)}";
            }

            if (!string.IsNullOrWhiteSpace(branchName))
            {
                return $"branch_{SanitizeForDocumentId(branchName)}";
            }

            throw new ArgumentException("Branch name or code is required");
        }

        /// <summary>
        /// Sanitizes a string to be used as part of a Firestore document ID
        /// </summary>
        private string SanitizeForDocumentId(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "unknown";

            // Convert to lowercase and replace invalid characters
            var sanitized = input
                .ToLowerInvariant()
                .Replace(" ", "_")
                .Replace("&", "and")
                .Replace("/", "_")
                .Replace("\\", "_")
                .Replace(".", "")
                .Replace(",", "")
                .Replace("'", "")
                .Replace("\"", "")
                .Replace("(", "")
                .Replace(")", "")
                .Replace("[", "")
                .Replace("]", "")
                .Replace("{", "")
                .Replace("}", "")
                .Replace("#", "")
                .Replace("$", "")
                .Replace("%", "")
                .Replace("^", "")
                .Replace("*", "")
                .Replace("+", "")
                .Replace("=", "")
                .Replace(":", "")
                .Replace(";", "")
                .Replace("?", "")
                .Replace("!", "")
                .Replace("@", "at")
                .Replace("|", "_")
                .Replace("<", "")
                .Replace(">", "");

            // Remove consecutive underscores
            while (sanitized.Contains("__"))
            {
                sanitized = sanitized.Replace("__", "_");
            }

            // Trim underscores from start and end
            sanitized = sanitized.Trim('_');

            // Ensure it's not empty after sanitization
            if (string.IsNullOrWhiteSpace(sanitized))
                return "unnamed";

            // Keep it reasonable (Firestore allows up to ~1500 bytes)
            if (sanitized.Length > 100)
                sanitized = sanitized.Substring(0, 100);

            return sanitized;
        }

        
        

        /// <summary>
        /// Save an ERP/structured user to "users" with ID = user_{FirebaseUid}
        /// </summary>
        public async Task SaveUserAccountAsync(UserAccount user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            if (string.IsNullOrEmpty(user.FirebaseUid))
                throw new ArgumentException("Firebase UID is required for user account");

            // Use the structured user ID format and ensure the object carries it
            string userId = GenerateUserId(user.FirebaseUid);
            user.Id = userId;

            await SetDocumentAsync("users", userId, user);
        }

        /// <summary>
        /// Save an online user to "online_users" with Email as the document ID.
        /// </summary>
        public async Task SaveOnlineUserAsync(UserAccount user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));
            if (string.IsNullOrWhiteSpace(user.Email))
                throw new ArgumentException("Email required for online user");

            var docId = user.Email.Trim(); // or .ToLowerInvariant() if you want case-insensitive keys
            user.Id = docId;
            await SetDocumentAsync("online_users", docId, user);
        }

        /// <summary>
        /// Get a user by ID with fallback search (from "users" collection, ID = user_{uid} or raw id).
        /// </summary>
        public async Task<UserAccount> GetUserByIdAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return null;

            try
            {
                // Handle both raw UID and structured user ID
                string docId = userId.StartsWith("user_") ? userId : GenerateUserId(userId);
                var user = await GetDocumentAsync<UserAccount>("users", docId);

                if (user != null)
                {
                    return user;
                }

                // If not found with structured ID, try the raw ID
                if (docId != userId)
                {
                    user = await GetDocumentAsync<UserAccount>("users", userId);
                    if (user != null)
                    {
                        return user;
                    }
                }

                return null;
            }
            catch (Exception)
            {
                // Swallow and return null to indicate not found
                return null;
            }
        }

        /// <summary>
        /// Get an online user by email-as-document-id from "online_users".
        /// </summary>
        public async Task<UserAccount> GetOnlineUserByEmailIdAsync(string emailAsDocId)
        {
            if (string.IsNullOrWhiteSpace(emailAsDocId))
                return null;

            var docId = emailAsDocId.Trim(); // or ToLowerInvariant() if normalized on write
            return await GetDocumentAsync<UserAccount>("online_users", docId);
        }

        /// <summary>
        /// Save an order/transaction with structured ID
        /// </summary>
        public async Task<string> SaveOrderAsync(POSTransaction transaction)
        {
            string orderId = await GenerateOrderIdAsync();
            transaction.TransactionId = orderId; // Store the ID in the transaction object
            await SetDocumentAsync("transactions", orderId, transaction);
            return orderId;
        }

        /// <summary>
        /// Get orders for a specific user
        /// </summary>
        public async Task<List<POSTransaction>> GetUserOrdersAsync(string userId)
        {
            var query = _firestoreDb.Collection("transactions")
                .WhereEqualTo("clientId", userId)  // Ensure field name matches your schema
                .OrderByDescending("timestamp");   // Ensure index exists when combining with filters

            var snapshot = await query.GetSnapshotAsync();
            var transactions = snapshot.Documents.Select(doc =>
            {
                var transaction = doc.ConvertTo<POSTransaction>();

                // Ensure TransactionId is set if missing
                if (string.IsNullOrEmpty(transaction.TransactionId))
                {
                    transaction.TransactionId = doc.Id;
                }

                return transaction;
            }).ToList();

            return transactions;
        }

        /// <summary>
        /// Get all orders with optional filtering
        /// </summary>
        public async Task<List<POSTransaction>> GetAllOrdersAsync(int? limit = null)
        {
            Query query = _firestoreDb.Collection("transactions")
                .OrderByDescending("timestamp");  // Ensure index exists if you add filters later

            if (limit.HasValue)
            {
                query = query.Limit(limit.Value);
            }

            var snapshot = await query.GetSnapshotAsync();
            var transactions = snapshot.Documents.Select(doc =>
            {
                var transaction = doc.ConvertTo<POSTransaction>();

                // Ensure TransactionId is set to document ID if missing
                if (string.IsNullOrEmpty(transaction.TransactionId))
                {
                    transaction.TransactionId = doc.Id;
                }

                return transaction;
            }).ToList();

            return transactions;
        }


        /// <summary>
        /// Get all users (ERP/structured) from "users" ordered by email.
        /// </summary>
        public async Task<List<UserAccount>> GetAllUsersAsync(int? limit = null)
        {
            Query query = _firestoreDb.Collection("users")
                .OrderBy("email"); // lower-case to match Firestore property

            if (limit.HasValue)
            {
                query = query.Limit(limit.Value);
            }

            var snapshot = await query.GetSnapshotAsync();
            var users = snapshot.Documents.Select(doc => doc.ConvertTo<UserAccount>()).ToList();
            return users;
        }

        /// <summary>
        /// Get all online users from "online_users", ordered by email.
        /// </summary>
        public async Task<List<UserAccount>> GetAllOnlineUsersAsync(int? limit = null)
        {
            Query query = _firestoreDb.Collection("online_users")
                .OrderBy("email");

            if (limit.HasValue)
            {
                query = query.Limit(limit.Value);
            }

            var snapshot = await query.GetSnapshotAsync();
            var users = snapshot.Documents.Select(doc => doc.ConvertTo<UserAccount>()).ToList();
            return users;
        }

        /// <summary>
        /// Get all contact messages with optional filtering
        /// </summary>
        public async Task<List<ContactMessage>> GetAllMessagesAsync(int? limit = null, bool? unreadOnly = null)
        {
            Query query = _firestoreDb.Collection("messages")
                .OrderByDescending("timestamp");

            if (unreadOnly == true)
            {
                query = query.WhereEqualTo("isRead", false);
            }

            if (limit.HasValue)
            {
                query = query.Limit(limit.Value);
            }

            var snapshot = await query.GetSnapshotAsync();
            return snapshot.Documents.Select(doc => doc.ConvertTo<ContactMessage>()).ToList();
        }

        /// <summary>
        /// Mark a contact message as read
        /// </summary>
        public async Task MarkMessageAsReadAsync(string messageId)
        {
            var docRef = _firestoreDb.Collection("messages").Document(messageId);
            await docRef.UpdateAsync("isRead", true);
        }

        /// <summary>
        /// Get a specific order by transaction ID
        /// </summary>
        public async Task<POSTransaction> GetOrderByIdAsync(string transactionId)
        {
            // try to find by TransactionId field
            var query = _firestoreDb.Collection("transactions")
                .WhereEqualTo("TransactionId", transactionId)
                .Limit(1);

            var snapshot = await query.GetSnapshotAsync();
            var result = snapshot.Documents.FirstOrDefault()?.ConvertTo<POSTransaction>();

            // try to find by document ID
            if (result == null)
            {
                result = await GetDocumentAsync<POSTransaction>("transactions", transactionId);
                if (result != null && string.IsNullOrEmpty(result.TransactionId))
                {
                    result.TransactionId = transactionId;
                }
            }

            return result;
        }

        /// <summary>
        /// Get a specific contact message by ID
        /// </summary>
        public async Task<ContactMessage> GetMessageByIdAsync(string messageId)
        {
            return await GetDocumentAsync<ContactMessage>("messages", messageId);
        }

        /// <summary>
        /// Get order statistics for dashboard
        /// </summary>
        public async Task<OrderStatistics> GetOrderStatisticsAsync()
        {
            var orders = await GetAllOrdersAsync();
            var today = DateTime.UtcNow.Date;
            var thisWeek = today.AddDays(-(int)today.DayOfWeek);
            var thisMonth = new DateTime(today.Year, today.Month, 1);

            return new OrderStatistics
            {
                TotalOrders = orders.Count,
                TotalRevenue = orders.Sum(o => o.GrandTotal),
                OrdersToday = orders.Count(o => o.Timestamp?.ToDateTime().Date == today),
                OrdersThisWeek = orders.Count(o => o.Timestamp?.ToDateTime().Date >= thisWeek),
                OrdersThisMonth = orders.Count(o => o.Timestamp?.ToDateTime().Date >= thisMonth)
            };
        }

        public class OrderStatistics
        {
            public int TotalOrders { get; set; }
            public double TotalRevenue { get; set; }
            public int OrdersToday { get; set; }
            public int OrdersThisWeek { get; set; }
            public int OrdersThisMonth { get; set; }
        }

      

        /// <summary>
        /// Copy existing online_users docs keyed by UID to email-as-ID, then delete old UID docs.
        /// Returns number of migrated docs.
        /// </summary>
        public async Task<int> MigrateOnlineUsersToEmailIdsAsync()
        {
            var col = _firestoreDb.Collection("online_users");
            var snap = await col.GetSnapshotAsync();
            int migrated = 0;

            foreach (var doc in snap.Documents)
            {
                // Skip if ID already looks like an email
                if (doc.Id.Contains("@")) continue;

                if (!doc.TryGetValue("email", out string email) || string.IsNullOrWhiteSpace(email))
                    continue;

                var newId = email.Trim(); // or .ToLowerInvariant()
                if (newId == doc.Id) continue;

                var data = doc.ToDictionary();
                await col.Document(newId).SetAsync(data, SetOptions.Overwrite);
                await doc.Reference.DeleteAsync();
                migrated++;
            }

            return migrated;
        }
    }
}