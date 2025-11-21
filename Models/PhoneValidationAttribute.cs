using System;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace OneClick_WebApp.Models.ValidationAttributes
{
    /// <summary>
    /// Validates phone numbers for South Africa (+27) and Mozambique (+258) only
    /// </summary>
    public class SAMozPhoneAttribute : ValidationAttribute
    {
        private static readonly Regex E164Regex = new Regex(@"^\+(?:27|258)\d{9}$", RegexOptions.Compiled);
        private static readonly Regex LocalSARegex = new Regex(@"^0[1-8]\d{8}$", RegexOptions.Compiled);
        private static readonly Regex LocalMozRegex = new Regex(@"^8[2-7]\d{7}$", RegexOptions.Compiled);

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
            {
                // Allow empty if not [Required]
                return ValidationResult.Success;
            }

            var phone = value.ToString().Trim();

            // Remove common formatting characters
            phone = Regex.Replace(phone, @"[\s\-\(\)]+", "");

            // Check E.164 format (+27 or +258)
            if (E164Regex.IsMatch(phone))
            {
                return ValidationResult.Success;
            }

            // Check local SA format (0xx xxx xxxx)
            if (LocalSARegex.IsMatch(phone))
            {
                // Could auto-convert to +27 format here if needed
                return ValidationResult.Success;
            }

            // Check local Mozambique format (8x xxx xxxx)
            if (LocalMozRegex.IsMatch(phone))
            {
                // Could auto-convert to +258 format here if needed
                return ValidationResult.Success;
            }

            return new ValidationResult(
                "Please enter a valid South African (+27) or Mozambican (+258) phone number. " +
                "Examples: +27821234567, 0821234567, +258841234567"
            );
        }

        /// <summary>
        /// Normalizes phone number to E.164 format
        /// </summary>
        public static string NormalizePhone(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return phone;

            phone = Regex.Replace(phone.Trim(), @"[\s\-\(\)]+", "");

            // Already in E.164 format
            if (phone.StartsWith("+"))
                return phone;

            // South African local format
            if (phone.StartsWith("0") && phone.Length == 10)
            {
                return "+27" + phone.Substring(1);
            }

            // Mozambique local format
            if (phone.StartsWith("8") && phone.Length == 9)
            {
                return "+258" + phone;
            }

            return phone;
        }
    }

    /// <summary>
    /// Validates that a string contains only safe business name characters
    /// </summary>
    public class SafeBusinessNameAttribute : ValidationAttribute
    {
        private static readonly Regex SafeNameRegex = new Regex(
            @"^[a-zA-Z0-9\s\-\.\,\&\'\(\)]+$",
            RegexOptions.Compiled
        );

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
            {
                return new ValidationResult("Company name is required.");
            }

            var name = value.ToString().Trim();

            if (name.Length < 2 || name.Length > 120)
            {
                return new ValidationResult("Company name must be between 2 and 120 characters.");
            }

            if (!SafeNameRegex.IsMatch(name))
            {
                return new ValidationResult(
                    "Company name can only contain letters, numbers, spaces, and common punctuation (- . , & ' ( ))."
                );
            }

            // Check for potential script injection
            if (name.Contains("<") || name.Contains(">") || name.Contains("script", StringComparison.OrdinalIgnoreCase))
            {
                return new ValidationResult("Company name contains invalid characters.");
            }

            return ValidationResult.Success;
        }
    }

    /// <summary>
    /// Enhanced URL validation with scheme requirement
    /// </summary>
    public class ValidUrlAttribute : ValidationAttribute
    {
        private readonly bool _requireHttps;

        public ValidUrlAttribute(bool requireHttps = false)
        {
            _requireHttps = requireHttps;
        }

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
            {
                return ValidationResult.Success; // Allow empty unless [Required]
            }

            var url = value.ToString().Trim();

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return new ValidationResult("Please enter a valid URL starting with http:// or https://");
            }

            if (_requireHttps && uri.Scheme != "https")
            {
                return new ValidationResult("URL must use HTTPS (https://)");
            }

            if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                return new ValidationResult("URL must start with http:// or https://");
            }

            // Additional validation for social media URLs
            var displayName = validationContext.DisplayName?.ToLower() ?? "";
            if (displayName.Contains("facebook") && !uri.Host.Contains("facebook"))
            {
                return new ValidationResult("Please enter a valid Facebook URL");
            }
            if (displayName.Contains("instagram") && !uri.Host.Contains("instagram"))
            {
                return new ValidationResult("Please enter a valid Instagram URL");
            }
            if (displayName.Contains("twitter") && !uri.Host.Contains("twitter") && !uri.Host.Contains("x.com"))
            {
                return new ValidationResult("Please enter a valid Twitter/X URL");
            }
            if (displayName.Contains("linkedin") && !uri.Host.Contains("linkedin"))
            {
                return new ValidationResult("Please enter a valid LinkedIn URL");
            }

            return ValidationResult.Success;
        }
    }
}