using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OneClick_WebApp.Services;
using FirebaseAdmin.Auth;
using System.ComponentModel.DataAnnotations;

namespace OneClick_WebApp.Controllers
{
    [ApiController]
    [Route("api")]
    public class ApiController : ControllerBase
    {
        private readonly FirebaseStorageService _storageService;
        private readonly ILogger<ApiController> _logger;
        private static readonly string[] AllowedImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        private static readonly string[] AuthorizedEmails = {
            "paymentportal.oneclicksolutions@gmail.com",
            "didmasabisai@gmail.com"
        };

        public ApiController(FirebaseStorageService storageService, ILogger<ApiController> logger)
        {
            _storageService = storageService;
            _logger = logger;
        }

        [HttpPost("verify-token")]
        public async Task<IActionResult> VerifyToken([FromBody] TokenRequest request)
        {
            try
            {
                var decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(request.Token);
                var userEmail = decodedToken.Claims.GetValueOrDefault("email")?.ToString();

                if (string.IsNullOrEmpty(userEmail) || !AuthorizedEmails.Contains(userEmail))
                {
                    return Unauthorized(new { error = "Unauthorized email address" });
                }

                return Ok(new
                {
                    success = true,
                    email = userEmail,
                    uid = decodedToken.Uid
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token verification failed");
                return Unauthorized(new { error = "Invalid token" });
            }
        }

        [HttpPost("upload-logo")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> UploadLogo([FromForm] IFormFile file, [FromForm] string fileName, [FromForm] string folder)
        {
            try
            {
                // Validate file
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new { error = "No file provided" });
                }

                if (file.Length > 5 * 1024 * 1024) // 5MB
                {
                    return BadRequest(new { error = "File size must be less than 5MB" });
                }

                var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
                if (string.IsNullOrEmpty(extension) || !AllowedImageExtensions.Contains(extension))
                {
                    return BadRequest(new { error = "Only image files (JPG, PNG, GIF, WebP) are allowed" });
                }

                // Verify user email authorization
                var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
                if (string.IsNullOrEmpty(userEmail) || !AuthorizedEmails.Contains(userEmail))
                {
                    return Unauthorized(new { error = "Unauthorized email address" });
                }

                // Upload file
                using (var stream = file.OpenReadStream())
                {
                    var logoUrl = await _storageService.UploadFileAsync(stream, fileName, folder);

                    _logger.LogInformation("Logo uploaded successfully by {Email}: {LogoUrl}", userEmail, logoUrl);

                    return Ok(new
                    {
                        success = true,
                        url = logoUrl,
                        message = "Logo uploaded successfully"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Logo upload failed");
                return StatusCode(500, new { error = $"Upload failed: {ex.Message}" });
            }
        }

        public class TokenRequest
        {
            [Required]
            public string Token { get; set; }
        }
    }
}