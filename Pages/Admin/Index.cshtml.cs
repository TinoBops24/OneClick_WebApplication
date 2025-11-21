using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OneClick_WebApp.Models;
using OneClick_WebApp.Models.Enums;
using OneClick_WebApp.Models.ValidationAttributes;
using OneClick_WebApp.Pages;
using OneClick_WebApp.Services;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Linq;

namespace OneClick_WebApp.Pages.Admin
{
    [Authorize(Policy = "AdminOnly")]
    public class IndexModel : BasePageModel
    {
        private readonly FirebaseStorageService _storageService;
        //private readonly CacheManagerService _cacheManager;
        private readonly ILogger<IndexModel> _logger;
        private readonly IConfiguration _configuration;
        private const int MaxFileSizeMB = 5;
        private static readonly string[] AllowedImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

        // Firebase config properties for client-side use
        public string FirebaseApiKey { get; private set; }
        public string FirebaseAuthDomain { get; private set; }
        public string FirebaseProjectId { get; private set; }
        public string FirebaseStorageBucket { get; private set; }

        public IndexModel(
            FirebaseDbService dbService,
            FirebaseStorageService storageService,
            IConfiguration configuration,
            ILogger<IndexModel> logger) : base(dbService)
        {
            _storageService = storageService;
            _configuration = configuration;
            _logger = logger;

            // Load Firebase config for client-side use
            FirebaseApiKey = configuration["Firebase:ApiKey"];
            FirebaseAuthDomain = configuration["Firebase:AuthDomain"];
            FirebaseProjectId = configuration["Firebase:ProjectId"];
            FirebaseStorageBucket = configuration["Firebase:StorageBucket"];
        }

        [BindProperty]
        public AdminConfigInputModel Input { get; set; }

        [BindProperty]
        public IFormFile Upload { get; set; }

        [BindProperty]
        public bool ReplaceExistingLogo { get; set; }

        
        [BindProperty(SupportsGet = true)]
        public string CurrentLogoUrl { get; set; }

        // Admin TempData to prevent bleeding into customer pages
        [TempData]
        public string AdminSuccessMessage { get; set; }

        [TempData]
        public string AdminErrorMessage { get; set; }

        public class AdminConfigInputModel
        {
            [Required(ErrorMessage = "Company Name is required.")]
            [SafeBusinessName]
            [Display(Name = "Company Name")]
            public string CompanyName { get; set; }

            [SAMozPhone]
            [Display(Name = "Primary Phone")]
            public string Phone { get; set; }

            [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
            [Display(Name = "Company Description")]
            public string Description { get; set; }

            [ValidUrl]
            [Display(Name = "Main Social Media Link")]
            public string SocialMediaLink { get; set; }

            [Required(ErrorMessage = "Primary colour is required.")]
            [RegularExpression(@"^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$", ErrorMessage = "Invalid colour format")]
            public string PrimaryColour { get; set; }

            [Required(ErrorMessage = "Secondary colour is required.")]
            [RegularExpression(@"^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$", ErrorMessage = "Invalid colour format")]
            public string SecondaryColour { get; set; }

            // About Us Section Fields
            [StringLength(100)]
            [Display(Name = "Section 1 Title")]
            public string AboutSection1_Title { get; set; }

            [StringLength(2000)]
            [Display(Name = "Section 1 Content")]
            public string AboutSection1_Content { get; set; }

            [StringLength(100)]
            [Display(Name = "Section 2 Title")]
            public string AboutSection2_Title { get; set; }

            [StringLength(2000)]
            [Display(Name = "Section 2 Content")]
            public string AboutSection2_Content { get; set; }

            [StringLength(100)]
            [Display(Name = "Section 3 Title")]
            public string AboutSection3_Title { get; set; }

            [StringLength(2000)]
            [Display(Name = "Section 3 Content")]
            public string AboutSection3_Content { get; set; }

            // Contact Page Settings
            [EmailAddress(ErrorMessage = "Please enter a valid email address")]
            [Display(Name = "Contact Form Recipient Email")]
            public string ContactFormRecipientEmail { get; set; }

            [SAMozPhone]
            [Display(Name = "Contact Page Phone")]
            public string ContactPagePhone { get; set; }

            [StringLength(200)]
            [Display(Name = "Contact Page Location")]
            public string ContactPageLocation { get; set; }

            [Required]
            [Display(Name = "Contact Form Handler")]
            public ContactFormHandler ContactFormHandler { get; set; }

            // Footer Content
            [StringLength(300)]
            [Display(Name = "Footer Tagline")]
            public string FooterTagline { get; set; }

            [EmailAddress]
            [Display(Name = "Support Email")]
            public string SupportEmail { get; set; }

            [SAMozPhone]
            [Display(Name = "Support Phone")]
            public string SupportPhone { get; set; }

            // Social Media Links with validation
            [ValidUrl]
            [Display(Name = "Facebook URL")]
            public string SocialMedia_Facebook { get; set; }

            [ValidUrl]
            [Display(Name = "Instagram URL")]
            public string SocialMedia_Instagram { get; set; }

            [ValidUrl]
            [Display(Name = "Twitter/X URL")]
            public string SocialMedia_Twitter { get; set; }

            [ValidUrl]
            [Display(Name = "LinkedIn URL")]
            public string SocialMedia_LinkedIn { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            await LoadSiteSettingsAsync();

            // Use structured document ID for company settings
            var config = await _dbService.GetBranchConfigurationAsync();

            if (config != null)
            {
                _logger.LogInformation("OnGetAsync - Loading config: CompanyName={CompanyName}, LogoUrl={LogoUrl}",
                    config.CompanyName, config.LogoUrl);

                Input = MapBranchToInputModel(config);
                CurrentLogoUrl = config.LogoUrl;

                _logger.LogInformation("OnGetAsync - Set Input.CompanyName={CompanyName}, CurrentLogoUrl={CurrentLogoUrl}",
                    Input.CompanyName, CurrentLogoUrl);
            }
            else
            {
                _logger.LogWarning("OnGetAsync - No config found, using defaults");
                Input = new AdminConfigInputModel
                {
                    PrimaryColour = "#007bff",
                    SecondaryColour = "#6c757d",
                    ContactFormHandler = ContactFormHandler.SaveToDatabase
                };
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            _logger.LogInformation("POST Index: ReplaceExistingLogo={ReplaceExistingLogo}, CurrentLogoUrl='{CurrentLogoUrl}', UploadNull={UploadNull}",
    ReplaceExistingLogo, CurrentLogoUrl, Upload == null);

            _logger.LogInformation(
    "POST Index: ReplaceExistingLogo={ReplaceExistingLogo}, CurrentLogoUrl='{CurrentLogoUrl}', UploadNull={UploadNull}",
    ReplaceExistingLogo, CurrentLogoUrl, Upload == null);

          

            // If we are NOT replacing and there IS a current logo, make Upload optional.
            if (!ReplaceExistingLogo && !string.IsNullOrEmpty(CurrentLogoUrl))
            {
                // Remove any automatic or stale 'required' error on Upload
                ModelState.Remove(nameof(Upload));
            }
            // ✅ Validate upload ONLY when user opted to replace the logo
            if (ReplaceExistingLogo)
            {
                if (Upload == null || Upload.Length == 0)
                {
                    ModelState.AddModelError("Upload", "Please select a logo image file.");
                }
                else if (!FirebaseStorageService.IsAllowedFileType(Upload.FileName, AllowedImageExtensions))
                {
                    ModelState.AddModelError("Upload", $"Only image files are allowed: {string.Join(", ", AllowedImageExtensions)}");
                }
                else if (Upload.Length > MaxFileSizeMB * 1024 * 1024)
                {
                    ModelState.AddModelError("Upload", $"File size cannot exceed {MaxFileSizeMB}MB.");
                }
            }

            if (!ModelState.IsValid)
            {
                // Reload current logo URL on validation failure
                var currentConfig = await _dbService.GetBranchConfigurationAsync();
                CurrentLogoUrl = currentConfig?.LogoUrl;
                await LoadSiteSettingsAsync();
                return Page();
            }

            try
            {
                // Get existing config or create new
                var configToUpdate = await _dbService.GetBranchConfigurationAsync() ?? new Branch();

                // Store old logo URL 
                var oldLogoUrl = configToUpdate.LogoUrl;

                // Map all input fields to the Branch model
                MapInputModelToBranch(Input, configToUpdate);

                // Handle logo upload if needed
                if ((ReplaceExistingLogo || string.IsNullOrEmpty(oldLogoUrl)) && Upload != null && Upload.Length > 0)
                {
                    try
                    {
                        // Generate structured filename
                        var companySlug = SanitiseCompanyNameForId(Input.CompanyName);
                        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                        var extension = Path.GetExtension(Upload.FileName).ToLowerInvariant();
                        var fileName = $"logo_{companySlug}_{timestamp}{extension}";

                        using (var stream = Upload.OpenReadStream())
                        {
                            var logoUrl = await _storageService.UploadFileAsync(stream, fileName, "site");
                            configToUpdate.LogoUrl = logoUrl;

                            _logger.LogInformation("Logo uploaded successfully: {LogoUrl}", logoUrl);
                        }

                        // Optionally delete old logo if replacing
                        if (ReplaceExistingLogo && !string.IsNullOrEmpty(oldLogoUrl) && oldLogoUrl != configToUpdate.LogoUrl)
                        {
                            _ = Task.Run(async () => await _storageService.DeleteFileAsync(oldLogoUrl));
                        }
                    }
                    catch (Exception uploadEx)
                    {
                        _logger.LogError(uploadEx, "Failed to upload logo");
                        ModelState.AddModelError("Upload", "Failed to upload logo. Please try again.");
                        CurrentLogoUrl = oldLogoUrl;
                        await LoadSiteSettingsAsync();
                        return Page();
                    }
                }

                // Save to Firestore with structured document ID
                await _dbService.SaveBranchConfigurationAsync(configToUpdate);

                // Refresh cache so changes reflect immediately
                //await _cacheManager.RefreshSiteSettingsCacheAsync();

                // Use admin-scoped TempData 
                AdminSuccessMessage = "Site configuration updated successfully!";
                _logger.LogInformation("Site configuration saved for company: {CompanyName}", Input.CompanyName);

                await LoadSiteSettingsAsync();

                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save site configuration");
                AdminErrorMessage = "An error occurred while saving. Please try again.";

                // Reload current state
                var currentConfig = await _dbService.GetBranchConfigurationAsync();
                CurrentLogoUrl = currentConfig?.LogoUrl;
                await LoadSiteSettingsAsync();

                return Page();
            }
        }

        private AdminConfigInputModel MapBranchToInputModel(Branch branch)
        {
            return new AdminConfigInputModel
            {
                CompanyName = branch.CompanyName,
                Phone = branch.Phone,
                Description = branch.Description,
                SocialMediaLink = branch.SocialMediaLink,
                PrimaryColour = branch.PrimaryColour ?? "#007bff",
                SecondaryColour = branch.SecondaryColour ?? "#6c757d",

                // About sections
                AboutSection1_Title = branch.AboutSection1_Title,
                AboutSection1_Content = branch.AboutSection1_Content,
                AboutSection2_Title = branch.AboutSection2_Title,
                AboutSection2_Content = branch.AboutSection2_Content,
                AboutSection3_Title = branch.AboutSection3_Title,
                AboutSection3_Content = branch.AboutSection3_Content,

                // Contact settings
                ContactFormRecipientEmail = branch.ContactFormRecipientEmail,
                ContactPagePhone = branch.ContactPagePhone,
                ContactPageLocation = branch.ContactPageLocation,
                ContactFormHandler = branch.ContactFormHandler,

                // Footer content
                FooterTagline = branch.FooterTagline,
                SupportEmail = branch.SupportEmail,
                SupportPhone = branch.SupportPhone,

                // Social media
                SocialMedia_Facebook = branch.SocialMedia_Facebook,
                SocialMedia_Instagram = branch.SocialMedia_Instagram,
                SocialMedia_Twitter = branch.SocialMedia_Twitter,
                SocialMedia_LinkedIn = branch.SocialMedia_LinkedIn
            };
        }

        private void MapInputModelToBranch(AdminConfigInputModel input, Branch branch)
        {
            //  normalise inputs
            branch.CompanyName = input.CompanyName?.Trim();
            branch.Phone = SAMozPhoneAttribute.NormalizePhone(input.Phone);
            branch.Description = input.Description?.Trim();
            branch.SocialMediaLink = NormaliseUrl(input.SocialMediaLink);
            branch.PrimaryColour = input.PrimaryColour;
            branch.SecondaryColour = input.SecondaryColour;

            // About sections
            branch.AboutSection1_Title = input.AboutSection1_Title?.Trim();
            branch.AboutSection1_Content = input.AboutSection1_Content?.Trim();
            branch.AboutSection2_Title = input.AboutSection2_Title?.Trim();
            branch.AboutSection2_Content = input.AboutSection2_Content?.Trim();
            branch.AboutSection3_Title = input.AboutSection3_Title?.Trim();
            branch.AboutSection3_Content = input.AboutSection3_Content?.Trim();

            // Contact settings
            branch.ContactFormRecipientEmail = input.ContactFormRecipientEmail?.Trim().ToLowerInvariant();
            branch.ContactPagePhone = SAMozPhoneAttribute.NormalizePhone(input.ContactPagePhone);
            branch.ContactPageLocation = input.ContactPageLocation?.Trim();
            branch.ContactFormHandler = input.ContactFormHandler;

            // Footer content
            branch.FooterTagline = input.FooterTagline?.Trim();
            branch.SupportEmail = input.SupportEmail?.Trim().ToLowerInvariant();
            branch.SupportPhone = SAMozPhoneAttribute.NormalizePhone(input.SupportPhone);

            // Social media - normalise URLs
            branch.SocialMedia_Facebook = NormaliseUrl(input.SocialMedia_Facebook);
            branch.SocialMedia_Instagram = NormaliseUrl(input.SocialMedia_Instagram);
            branch.SocialMedia_Twitter = NormaliseUrl(input.SocialMedia_Twitter);
            branch.SocialMedia_LinkedIn = NormaliseUrl(input.SocialMedia_LinkedIn);
        }

        private string NormaliseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            url = url.Trim();

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            return url;
        }

        private string SanitiseCompanyNameForId(string companyName)
        {
            if (string.IsNullOrWhiteSpace(companyName))
                return "company";

            return companyName
                .ToLowerInvariant()
                .Replace(" ", "_")
                .Replace("&", "and")
                .Replace("'", "")
                .Replace(".", "")
                .Replace(",", "")
                .Replace("(", "")
                .Replace(")", "")
                .Replace("-", "_");
        }
    }
}
