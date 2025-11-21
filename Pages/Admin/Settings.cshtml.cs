using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OneClick_WebApp.Models;
using OneClick_WebApp.Models.Enums;
using OneClick_WebApp.Services;
using System.ComponentModel.DataAnnotations;

namespace OneClick_WebApp.Pages.Admin
{
    [Authorize(Policy = "AdminOnly")]
    public class SettingsModel : BasePageModel
    {
        private readonly ILogger<SettingsModel> _logger;
        private readonly POSIntegrationService _posService;

        public SettingsModel(FirebaseDbService dbService, ILogger<SettingsModel> logger, POSIntegrationService posService) : base(dbService)
        {
            _logger = logger;
            _posService = posService;
        }

        [BindProperty]
        public POSSettingsInputModel Input { get; set; }

        [TempData]
        public string SuccessMessage { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public class POSSettingsInputModel
        {
            // === POS INTEGRATION SETTINGS ===
            [Display(Name = "Enable POS Integration")]
            public bool PosIntegrationEnabled { get; set; }

            [Display(Name = "POS System Type")]
            public POSSystemType PosSystemType { get; set; } = POSSystemType.Internal;

            [Display(Name = "Branch ID")]
            [StringLength(50, ErrorMessage = "Branch ID cannot exceed 50 characters")]
            [Required(ErrorMessage = "Branch ID is required for POS integration")]
            public string BranchId { get; set; } = "default_branch";

            [Display(Name = "Auto-Create POS Transactions")]
            public bool AutoCreatePosTransactions { get; set; } = true;

            [Display(Name = "POS Transaction Prefix")]
            [StringLength(10, ErrorMessage = "Prefix cannot exceed 10 characters")]
            public string PosTransactionPrefix { get; set; } = "POS";

            [Display(Name = "Enable Stock Validation")]
            public bool EnableStockValidation { get; set; } = true;

            [EmailAddress(ErrorMessage = "Please enter a valid email address")]
            [Display(Name = "POS Notification Email")]
            public string PosNotificationEmail { get; set; }

            // === BUSINESS SETTINGS ===
            [Display(Name = "Business Type")]
            public POSStyle BusinessType { get; set; } = POSStyle.Retail;

            [Display(Name = "Enable Discounts")]
            public bool EnableDiscount { get; set; }

            [Range(0, 100, ErrorMessage = "IVA percentage must be between 0 and 100")]
            [Display(Name = "Default IVA Percentage")]
            public double IVAPercentage { get; set; }

            [Display(Name = "Stock Notification Type")]
            public NotificationType StockNotification { get; set; }

            [Display(Name = "Product Ordering Style")]
            public ProductOrdering ProductOrderingStyle { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            await LoadSiteSettingsAsync();

            var config = await _dbService.GetBranchConfigurationAsync();

            if (config != null)
            {
                Input = new POSSettingsInputModel
                {
                    // POS Integration Settings
                    PosIntegrationEnabled = config.PosIntegrationEnabled,
                    PosSystemType = config.PosSystemType,
                    BranchId = config.BranchId ?? "default_branch", // Updated to use BranchId
                    AutoCreatePosTransactions = config.AutoCreatePosTransactions,
                    PosTransactionPrefix = config.PosTransactionPrefix ?? "POS",
                    EnableStockValidation = config.EnableStockValidation,
                    PosNotificationEmail = config.PosNotificationEmail,

                    // Business Settings
                    BusinessType = config.BusinessType,
                    EnableDiscount = config.EnableDiscount,
                    IVAPercentage = config.IVAPercentage,
                    StockNotification = config.StockNotification,
                    ProductOrderingStyle = config.ProductOrderingStyle
                };
            }
            else
            {
                // Initialize with defaults
                Input = new POSSettingsInputModel();
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await LoadSiteSettingsAsync();
                return Page();
            }

            try
            {
                // Get existing config or create new
                var config = await _dbService.GetBranchConfigurationAsync() ?? new Branch();

                // Update POS Integration settings
                config.PosIntegrationEnabled = Input.PosIntegrationEnabled;
                config.PosSystemType = Input.PosSystemType;
                config.BranchId = Input.BranchId?.Trim() ?? "default_branch"; // Store BranchId instead of collection name
                config.AutoCreatePosTransactions = Input.AutoCreatePosTransactions;
                config.PosTransactionPrefix = Input.PosTransactionPrefix?.Trim() ?? "POS";
                config.EnableStockValidation = Input.EnableStockValidation;
                config.PosNotificationEmail = Input.PosNotificationEmail?.Trim();

                // Update Business settings
                config.BusinessType = Input.BusinessType;
                config.EnableDiscount = Input.EnableDiscount;
                config.IVAPercentage = Input.IVAPercentage;
                config.StockNotification = Input.StockNotification;
                config.ProductOrderingStyle = Input.ProductOrderingStyle;

                // Save to Firestore
                await _dbService.SaveBranchConfigurationAsync(config);

                SuccessMessage = "Settings updated successfully!";
                _logger.LogInformation("POS and business settings updated. POS Integration: {PosEnabled}, Branch: {BranchId}",
                    Input.PosIntegrationEnabled, Input.BranchId);

                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save POS settings");
                ErrorMessage = "An error occurred while saving settings. Please try again.";
                await LoadSiteSettingsAsync();
                return Page();
            }
        }

        public async Task<JsonResult> OnPostTestPosConnectionAsync()
        {
            try
            {
                // Create a test transaction to verify the POS integration works
                var testCustomer = new CustomerInfo
                {
                    ID = "test_customer",
                    Name = "Test Customer",
                    WebName = "Test User",
                    PhoneNumber = "+27123456789"
                };

                // Create test transaction
                var testTransaction = await _posService.CreateTransactionAsync(Input.BranchId, testCustomer);
                testTransaction.Instructions = "POS Connection Test";
                testTransaction.GrandTotal = 0.01; // Minimal test amount

                // Try to save to POS system using the exact Python structure
                await _posService.SaveToPOSAsync(Input.BranchId, testTransaction);

                // Clean up test transaction
                var collectionPath = $"onlinesale/{Input.BranchId}/transaction";
                await _dbService.DeleteDocumentAsync(collectionPath, testTransaction.TransactionId);

                return new JsonResult(new
                {
                    success = true,
                    message = $"POS connection successful! Test transaction saved to: onlinesale/{Input.BranchId}/transaction"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "POS connection test failed for branch {BranchId}", Input.BranchId);
                return new JsonResult(new
                {
                    success = false,
                    message = $"POS connection test failed: {ex.Message}\nExpected path: onlinesale/{Input.BranchId}/transaction"
                });
            }
        }
    }
}