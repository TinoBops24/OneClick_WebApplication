using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OneClick_WebApp.Models;
using OneClick_WebApp.Services;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Google.Cloud.Firestore;

namespace OneClick_WebApp.Pages.Account
{
    [Authorize]
    public class OrderHistoryModel : BasePageModel
    {
        public List<OrderDisplayModel> Orders { get; set; } = new();

        public class OrderDisplayModel
        {
            public POSTransaction Transaction { get; set; }
            public string Phone { get; set; } = "N/A";
            public string Address { get; set; } = "N/A";
            public string DeliveryType { get; set; } = "Standard";
            public string FulfillmentStatus { get; set; } = "Pending";
        }

        public OrderHistoryModel(FirebaseDbService dbService) : base(dbService) { }

        public async Task OnGetAsync()
        {
            await LoadSiteSettingsAsync();

            // Use email instead of Firebase UID for order lookup
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
            {
                return;
            }

            try
            {
                // SUSTAINABLE FIX: Remove OrderBy to avoid composite index requirement
                // Sort in memory instead to ensure compatibility across all environments
                var transQuery = _dbService.GetCollection("transactions")
                    .WhereEqualTo("clientId", userEmail);
                // Removed .OrderByDescending("timestamp") to avoid index requirement

                var transSnapshot = await transQuery.GetSnapshotAsync();
                var transactions = transSnapshot.Documents.Select(doc =>
                {
                    var transaction = doc.ConvertTo<POSTransaction>();

                    // Ensure TransactionId is set if missing
                    if (string.IsNullOrEmpty(transaction.TransactionId))
                    {
                        transaction.TransactionId = doc.Id;
                    }

                    return transaction;
                }).ToList();

                if (!transactions.Any())
                {
                    // Log for debugging - user has no orders
                    Console.WriteLine($"No orders found for user email: {userEmail}");
                    return;
                }

                // Process transactions with the new model structure
                foreach (var transaction in transactions)
                {
                    Orders.Add(new OrderDisplayModel
                    {
                        Transaction = transaction,
                        // Use properties directly from POSTransaction
                        Phone = transaction.ClientPhoneNumber ?? transaction.Phone ?? "TBC",
                        Address = transaction.ClientAddress ?? transaction.Address ?? transaction.DeliveryAddress ?? "TBC",
                        DeliveryType = transaction.DeliveryType.ToString(),
                        FulfillmentStatus = transaction.FulfillmentStatus.ToString()
                    });
                }

                // SUSTAINABLE SORTING: Sort in memory by timestamp (most recent first)
                // This avoids Firestore composite index requirements while maintaining performance
                Orders = Orders.OrderByDescending(o =>
                {
                    if (o.Transaction.Timestamp.HasValue)
                        return o.Transaction.Timestamp.Value.ToDateTime();

                    // Fallback: try to extract date from TransactionId if timestamp missing
                    if (!string.IsNullOrEmpty(o.Transaction.TransactionId) && o.Transaction.TransactionId.Length >= 8)
                    {
                        // Attempt to parse date from TransactionId format (if it contains date info)
                        if (System.DateTime.TryParseExact(o.Transaction.TransactionId.Substring(0, 8),
                            "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var extractedDate))
                        {
                            return extractedDate;
                        }
                    }

                    return System.DateTime.MinValue; // Oldest entries without valid timestamps go to bottom
                }).ToList();

                Console.WriteLine($"Successfully loaded {Orders.Count} orders for user: {userEmail}");
            }
            catch (System.Exception ex)
            {
                // Log the specific error for debugging
                Console.WriteLine($"Error loading orders for user {userEmail}: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                // Ensure Orders is empty on error
                Orders = new List<OrderDisplayModel>();
            }
        }
    }
}