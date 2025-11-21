using Microsoft.AspNetCore.Mvc;
using OneClick_WebApp.Models;
using OneClick_WebApp.Services;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using System.Text.Json;
using System.Net.Http;

namespace OneClick_WebApp.Pages
{
    public class ContactModel : BasePageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        // Properties to hold dynamic contact information displayed on the page
        public string ContactPhone { get; set; }
        public string ContactLocation { get; set; }
        public string CompanyName { get; set; }

        // Geocoding properties used to position the Google Map marker
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        // Indicates whether valid coordinates were obtained from geocoding
        public bool HasValidCoordinates => Latitude.HasValue && Longitude.HasValue;

        public ContactModel(FirebaseDbService dbService, IHttpClientFactory httpClientFactory, IConfiguration configuration) : base(dbService)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        [TempData]
        public string SuccessMessage { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Your name is required.")]
            [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters.")]
            public string Name { get; set; }

            [Required(ErrorMessage = "Your email address is required.")]
            [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
            public string Email { get; set; }

            [Required(ErrorMessage = "A subject is required.")]
            [StringLength(150, ErrorMessage = "Subject cannot exceed 150 characters.")]
            public string Subject { get; set; }

            [Required(ErrorMessage = "A message is required.")]
            [StringLength(2000, ErrorMessage = "Message cannot exceed 2000 characters.")]
            public string Message { get; set; }
        }

        public async Task OnGetAsync()
        {
            await LoadSiteSettingsAsync();
            await LoadContactDataAsync();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            await LoadSiteSettingsAsync();

            // Reload contact information for display after form submission
            var branchConfig = await _dbService.GetBranchConfigurationAsync();
            if (branchConfig != null)
            {
                ContactPhone = branchConfig.ContactPagePhone ?? branchConfig.Phone;
                ContactLocation = branchConfig.ContactPageLocation ?? "Please contact us for location details.";
                CompanyName = branchConfig.CompanyName ?? "Our Business";

                // Get coordinates for the location if available
                if (!string.IsNullOrEmpty(ContactLocation) && ContactLocation != "Please contact us for location details.")
                {
                    var coordinates = await GetCoordinatesAsync(ContactLocation);
                    Latitude = coordinates.latitude;
                    Longitude = coordinates.longitude;
                }
            }

            // Set fallback coordinates if geocoding failed
            if (!HasValidCoordinates)
            {
                Latitude = -33.9249;
                Longitude = 18.4241;
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            try
            {
                // Create contact message with proper scoping - AdminOnly ensures it never appears on customer pages
                var message = new ContactMessage
                {
                    Name = Input.Name,
                    Email = Input.Email,
                    Subject = Input.Subject,
                    Message = Input.Message,
                    Timestamp = Timestamp.GetCurrentTimestamp(),
                    MessageScope = MessageScope.AdminOnly,  
                    MessageCategory = MessageCategory.ContactForm,
                    IsRead = false
                };

                await _dbService.AddDocumentAsync("messages", message);

                SuccessMessage = "Thank you for your message! We will get back to you shortly.";
                return RedirectToPage();
            }
            catch (System.Exception ex)
            {
                // Log error internally but show user-friendly message
                Console.WriteLine($"Error saving contact message: {ex.Message}");
                ModelState.AddModelError(string.Empty, "There was an error sending your message. Please try again.");
                return Page();
            }
        }

        /// <summary>
        /// Loads contact information from Firestore and performs geocoding to get map coordinates
        /// This method is called by both GET and POST handlers to ensure data consistency
        /// </summary>
        private async Task LoadContactDataAsync()
        {
            // Retrieve the branch configuration which contains contact details
            var branchConfig = await _dbService.GetBranchConfigurationAsync();

            if (branchConfig != null)
            {
                // Map contact details from Firestore to page properties
                ContactPhone = branchConfig.ContactPagePhone ?? branchConfig.Phone;
                ContactLocation = branchConfig.ContactPageLocation ?? "Please contact us for location details.";
                CompanyName = branchConfig.CompanyName ?? "Our Business";

                // Attempt geocoding only if we have a valid location string
                if (!string.IsNullOrEmpty(ContactLocation) &&
                    ContactLocation != "Please contact us for location details.")
                {
                    var coordinates = await GetCoordinatesAsync(ContactLocation);
                    Latitude = coordinates.latitude;
                    Longitude = coordinates.longitude;

                    // Log the geocoding result for debugging purposes
                    if (HasValidCoordinates)
                    {
                        // Use invariant culture to ensure proper decimal formatting with dots, not commas
                        Console.WriteLine($"Geocoding successful: {ContactLocation} -> ({Latitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {Longitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
                    }
                    else
                    {
                        Console.WriteLine($"Geocoding failed for: {ContactLocation}");
                    }
                }
            }
            else
            {
                // Fallback values if no configuration exists in Firestore
                ContactPhone = "Contact details will be available soon.";
                ContactLocation = "Location details will be available soon.";
                CompanyName = "Our Business";
            }

            // Always provide fallback coordinates (Cape Town city centre) if geocoding fails
            // This ensures the map always displays something rather than failing completely
            if (!HasValidCoordinates)
            {
                Latitude = -33.9249;  // Cape Town latitude
                Longitude = 18.4241;  // Cape Town longitude
                Console.WriteLine("Using fallback coordinates: Cape Town city centre");
            }
        }

        /// <summary>
        /// Converts a location string to geographic coordinates using geocoding APIs
        /// Tries Google Geocoding API first, then falls back to free Nominatim service
        /// </summary>
        /// <param name="location">The location string to geocode (e.g., "Rondebosch, Cape Town, 7700")</param>
        /// <returns>A tuple containing latitude and longitude, or (null, null) if geocoding fails</returns>
        private async Task<(double? latitude, double? longitude)> GetCoordinatesAsync(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
                return (null, null);

            try
            {
                // Primary geocoding method: Google Geocoding API (more accurate and reliable)
                var googleResult = await TryGoogleGeocodingAsync(location);
                if (googleResult.latitude.HasValue && googleResult.longitude.HasValue)
                {
                    return googleResult;
                }

                // Fallback geocoding method: Nominatim (free OpenStreetMap service)
                Console.WriteLine("Google Geocoding returned no results, trying Nominatim fallback");
                return await TryNominatimGeocodingAsync(location);
            }
            catch (Exception ex)
            {
                // Log the error but don't throw - geocoding failure shouldn't break the page
                Console.WriteLine($"Geocoding error for location '{location}': {ex.Message}");
                return (null, null);
            }
        }

        /// <summary>
        /// Attempts to geocode a location using the Google Geocoding API
        /// Requires a valid API key with Geocoding API enabled
        /// </summary>
        private async Task<(double? latitude, double? longitude)> TryGoogleGeocodingAsync(string location)
        {
            try
            {
                
                var apiKey = " ";
                var url = $"https://maps.googleapis.com/maps/api/geocode/json?address={Uri.EscapeDataString(location)}&key={apiKey}";

                using var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                var response = await httpClient.GetStringAsync(url);
                var googleResult = JsonSerializer.Deserialize<GoogleGeocodingResponse>(response, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Check if the API returned successful results
                if (googleResult?.Status == "OK" && googleResult.Results?.Length > 0)
                {
                    var firstResult = googleResult.Results[0];
                    return (firstResult.Geometry?.Location?.Lat, firstResult.Geometry?.Location?.Lng);
                }
                else
                {
                    Console.WriteLine($"Google Geocoding API returned status: {googleResult?.Status}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Google Geocoding API request failed: {ex.Message}");
            }

            return (null, null);
        }

        /// <summary>
        /// Attempts to geocode a location using the free Nominatim service from OpenStreetMap
        /// This is used as a fallback when Google Geocoding fails
        /// Note: Nominatim has usage limits - don't abuse this free service
        /// </summary>
        private async Task<(double? latitude, double? longitude)> TryNominatimGeocodingAsync(string location)
        {
            try
            {
                var url = $"https://nominatim.openstreetmap.org/search?format=json&q={Uri.EscapeDataString(location)}&limit=1";

                using var httpClient = _httpClientFactory.CreateClient();
                // Nominatim requires a User-Agent header as per their usage policy
                httpClient.DefaultRequestHeaders.Add("User-Agent", "OneClick_WebApp/1.0");
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                var response = await httpClient.GetStringAsync(url);
                var results = JsonSerializer.Deserialize<NominatimResult[]>(response, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Nominatim returns coordinates as strings, so we need to parse them
                if (results?.Length > 0)
                {
                    var result = results[0];
                    if (double.TryParse(result.Lat, out var lat) && double.TryParse(result.Lon, out var lng))
                    {
                        return (lat, lng);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Nominatim Geocoding request failed: {ex.Message}");
            }

            return (null, null);
        }

        #region Geocoding API Response Models

        // Data transfer objects for deserialising Google Geocoding API JSON responses
        public class GoogleGeocodingResponse
        {
            public string Status { get; set; }
            public GoogleResult[] Results { get; set; }
        }

        public class GoogleResult
        {
            public GoogleGeometry Geometry { get; set; }
        }

        public class GoogleGeometry
        {
            public GoogleLocation Location { get; set; }
        }

        public class GoogleLocation
        {
            public double Lat { get; set; }
            public double Lng { get; set; }
        }

        // Data transfer objects for deserialising Nominatim API JSON responses
        public class NominatimResult
        {
            public string Lat { get; set; }
            public string Lon { get; set; }
        }

        #endregion
    }
}