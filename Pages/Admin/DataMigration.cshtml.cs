using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OneClick_WebApp.Models;
using OneClick_WebApp.Pages;
using OneClick_WebApp.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OneClick_WebApp.Pages.Admin
{
    [Authorize(Policy = "AdminOnly")]
    public class DataMigrationModel : BasePageModel
    {
        private readonly ILogger<DataMigrationModel> _logger;

        public DataMigrationModel(FirebaseDbService dbService, ILogger<DataMigrationModel> logger)
            : base(dbService)
        {
            _logger = logger;
        }

        [TempData]
        public string StatusMessage { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public MigrationStatus Status { get; set; } = new();

        public async Task OnGetAsync()
        {
            await LoadSiteSettingsAsync();
            await LoadMigrationStatusAsync();
        }

        public async Task<IActionResult> OnPostAnalyzeOrdersAsync()
        {
            try
            {
                var analysisResult = await AnalyzeOrdersForMigrationAsync();
                Status = analysisResult;

                if (analysisResult.OrdersNeedingMigration > 0)
                {
                    StatusMessage = $"Analysis complete: {analysisResult.OrdersNeedingMigration} orders need migration out of {analysisResult.TotalOrders} total orders.";
                }
                else
                {
                    StatusMessage = "Analysis complete: All orders are already using email format. No migration needed.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order analysis failed");
                ErrorMessage = $"Analysis failed: {ex.Message}";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostMigrateOrderClientIdsAsync()
        {
            try
            {
                _logger.LogInformation("Starting order ClientId migration...");

                var migrationResult = await MigrateOrderClientIdsToEmailAsync();

                if (migrationResult.Success)
                {
                    StatusMessage = $"Migration completed successfully! " +
                                  $"Updated {migrationResult.MigratedCount} orders from Firebase UID to email format. " +
                                  $"Skipped {migrationResult.SkippedCount} orders that were already correct.";

                    _logger.LogInformation("Order migration completed successfully. Migrated: {MigratedCount}, Skipped: {SkippedCount}",
                        migrationResult.MigratedCount, migrationResult.SkippedCount);
                }
                else
                {
                    ErrorMessage = $"Migration completed with issues: {migrationResult.ErrorMessage}. " +
                                 $"Migrated: {migrationResult.MigratedCount}, Failed: {migrationResult.FailedCount}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order migration failed");
                ErrorMessage = $"Migration failed: {ex.Message}";
            }

            return RedirectToPage();
        }

        private async Task LoadMigrationStatusAsync()
        {
            try
            {
                Status = await AnalyzeOrdersForMigrationAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load migration status");
                Status = new MigrationStatus { ErrorMessage = "Unable to analyze current status" };
            }
        }

        private async Task<MigrationStatus> AnalyzeOrdersForMigrationAsync()
        {
            var status = new MigrationStatus();

            try
            {
                var allOrders = await _dbService.GetAllOrdersAsync();
                status.TotalOrders = allOrders.Count;

                foreach (var order in allOrders)
                {
                    if (string.IsNullOrEmpty(order.ClientId))
                    {
                        status.OrdersWithoutClientId++;
                    }
                    else if (IsFirebaseUidFormat(order.ClientId))
                    {
                        status.OrdersNeedingMigration++;
                        status.SampleFirebaseUids.Add($"{order.TransactionId}: {order.ClientId}");
                    }
                    else if (order.ClientId.Contains("@"))
                    {
                        status.OrdersAlreadyUsingEmail++;
                        status.SampleEmails.Add($"{order.TransactionId}: {order.ClientId}");
                    }
                    else
                    {
                        status.OrdersWithOtherFormat++;
                    }
                }

                // Limit sample sizes for display
                status.SampleFirebaseUids = status.SampleFirebaseUids.Take(5).ToList();
                status.SampleEmails = status.SampleEmails.Take(5).ToList();

                status.Success = true;
            }
            catch (Exception ex)
            {
                status.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Failed to analyze orders for migration");
            }

            return status;
        }

        private async Task<MigrationResult> MigrateOrderClientIdsToEmailAsync()
        {
            var result = new MigrationResult();

            try
            {
                // Load all data needed for migration
                var allOrders = await _dbService.GetAllOrdersAsync();
                var erpUsers = await _dbService.GetAllDocumentsAsync<UserAccount>("users");
                var onlineUsers = await _dbService.GetAllDocumentsAsync<UserAccount>("online_users");

                // Combine all users for lookup
                var allUsers = new List<UserAccount>();
                allUsers.AddRange(erpUsers);
                allUsers.AddRange(onlineUsers);

                _logger.LogInformation("Starting migration: {TotalOrders} orders, {TotalUsers} users",
                    allOrders.Count, allUsers.Count);

                foreach (var order in allOrders)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(order.ClientId))
                        {
                            result.SkippedCount++;
                            continue;
                        }

                        // Check if ClientId is already in email format
                        if (order.ClientId.Contains("@"))
                        {
                            result.SkippedCount++;
                            continue;
                        }

                        // Check if ClientId looks like Firebase UID (long string without @)
                        if (IsFirebaseUidFormat(order.ClientId))
                        {
                            // Find user by Firebase UID
                            var user = allUsers.FirstOrDefault(u =>
                                string.Equals(u.FirebaseUid, order.ClientId, StringComparison.OrdinalIgnoreCase));

                            if (user != null && !string.IsNullOrEmpty(user.Email))
                            {
                                _logger.LogInformation("Migrating order {OrderId}: {OldClientId} ? {NewClientId}",
                                    order.TransactionId, order.ClientId, user.Email);

                                // Update the order
                                var oldClientId = order.ClientId;
                                order.ClientId = user.Email;

                                // Save the updated order
                                await _dbService.SetDocumentAsync("transactions", order.Id, order);

                                result.MigratedCount++;
                                result.MigrationDetails.Add($"? {order.TransactionId}: {oldClientId} ? {user.Email}");
                            }
                            else
                            {
                                _logger.LogWarning("Could not find user for Firebase UID: {FirebaseUid} in order {OrderId}",
                                    order.ClientId, order.TransactionId);
                                result.FailedCount++;
                                result.FailureDetails.Add($"? {order.TransactionId}: User not found for UID {order.ClientId}");
                            }
                        }
                        else
                        {
                            // Unknown format - skip
                            result.SkippedCount++;
                        }
                    }
                    catch (Exception orderEx)
                    {
                        _logger.LogError(orderEx, "Failed to migrate order {OrderId}", order.TransactionId);
                        result.FailedCount++;
                        result.FailureDetails.Add($"? {order.TransactionId}: {orderEx.Message}");
                    }
                }

                // Limit details for display
                result.MigrationDetails = result.MigrationDetails.Take(20).ToList();
                result.FailureDetails = result.FailureDetails.Take(10).ToList();

                result.Success = result.FailedCount == 0;
                if (result.FailedCount > 0)
                {
                    result.ErrorMessage = $"{result.FailedCount} orders failed to migrate";
                }

                _logger.LogInformation("Migration completed: Migrated={Migrated}, Skipped={Skipped}, Failed={Failed}",
                    result.MigratedCount, result.SkippedCount, result.FailedCount);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Migration process failed");
            }

            return result;
        }

        private static bool IsFirebaseUidFormat(string clientId)
        {
            // Firebase UIDs are typically 28 characters long, alphanumeric, no @ symbol
            return !string.IsNullOrEmpty(clientId) &&
                   clientId.Length > 20 &&
                   !clientId.Contains("@") &&
                   clientId.All(c => char.IsLetterOrDigit(c));
        }

        public class MigrationStatus
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public int TotalOrders { get; set; }
            public int OrdersNeedingMigration { get; set; }
            public int OrdersAlreadyUsingEmail { get; set; }
            public int OrdersWithoutClientId { get; set; }
            public int OrdersWithOtherFormat { get; set; }
            public List<string> SampleFirebaseUids { get; set; } = new();
            public List<string> SampleEmails { get; set; } = new();
        }

        public class MigrationResult
        {
            public bool Success { get; set; }
            public int MigratedCount { get; set; }
            public int SkippedCount { get; set; }
            public int FailedCount { get; set; }
            public string ErrorMessage { get; set; }
            public List<string> MigrationDetails { get; set; } = new();
            public List<string> FailureDetails { get; set; } = new();
        }
    }
}