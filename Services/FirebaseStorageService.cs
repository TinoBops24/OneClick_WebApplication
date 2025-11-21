using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Google.Apis.Storage.v1.Data;

namespace OneClick_WebApp.Services
{
    public class FirebaseStorageService
    {
        private readonly StorageClient _storageClient;
        private readonly string _bucketName;
        private readonly ILogger<FirebaseStorageService> _logger;

        public FirebaseStorageService(IConfiguration configuration, ILogger<FirebaseStorageService> logger)
        {
            _bucketName = configuration["Firebase:StorageBucket"];
            _logger = logger;

            try
            {
                
                _storageClient = StorageClient.Create();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Firebase Storage client");
                throw;
            }
        }

        /// <summary>
        /// Uploads a file to Firebase Storage and returns a public URL
        /// </summary>
        public async Task<string> UploadFileAsync(Stream stream, string fileName, string folder)
        {
            if (stream == null || stream.Length == 0)
            {
                throw new ArgumentException("Stream is null or empty", nameof(stream));
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("Filename cannot be empty", nameof(fileName));
            }

            // Clean folder and filename
            folder = SanitizePath(folder ?? "uploads");
            fileName = SanitizeFileName(fileName);

            var objectName = $"{folder}/{fileName}";

            try
            {
                _logger.LogInformation("Uploading file to Firebase Storage: {ObjectName}", objectName);

                //  Upload the file
                var uploadedObject = await _storageClient.UploadObjectAsync(
                    bucket: _bucketName,
                    objectName: objectName,
                    contentType: GetContentType(fileName),
                    source: stream,
                    options: new UploadObjectOptions
                    {
                        PredefinedAcl = PredefinedObjectAcl.PublicRead // Make public on upload
                    }
                );

                //  Construct the public URL
                var publicUrl = $"https://storage.googleapis.com/{_bucketName}/{objectName}";

                _logger.LogInformation("File uploaded successfully: {PublicUrl}", publicUrl);

                return publicUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload file: {ObjectName}", objectName);
                throw new InvalidOperationException($"Failed to upload file: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Deletes a file from Firebase Storage (optional - for replacing logos)
        /// </summary>
        public async Task<bool> DeleteFileAsync(string fileUrl)
        {
            if (string.IsNullOrWhiteSpace(fileUrl))
                return false;

            try
            {
                // Extract object name from URL
                var prefix = $"https://storage.googleapis.com/{_bucketName}/";
                if (fileUrl.StartsWith(prefix))
                {
                    var objectName = fileUrl.Substring(prefix.Length);
                    await _storageClient.DeleteObjectAsync(_bucketName, objectName);
                    _logger.LogInformation("Deleted file: {ObjectName}", objectName);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete file: {FileUrl}", fileUrl);
            }

            return false;
        }

        /// <summary>
        /// Validates if a file extension is allowed for upload
        /// </summary>
        public static bool IsAllowedFileType(string fileName, string[] allowedExtensions = null)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            allowedExtensions ??= new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

            var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
            return !string.IsNullOrEmpty(extension) && Array.Exists(allowedExtensions, ext => ext == extension);
        }

        /// <summary>
        /// Gets content type based on file extension
        /// </summary>
        private string GetContentType(string fileName)
        {
            var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
        }

        /// <summary>
        /// Sanitizes filename to prevent path traversal attacks
        /// </summary>
        private string SanitizeFileName(string fileName)
        {
            // Remove path characters and keep only filename
            fileName = Path.GetFileName(fileName);

            // Replace spaces and special characters
            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
            {
                fileName = fileName.Replace(c, '_');
            }

            return fileName.Replace(" ", "_").ToLowerInvariant();
        }

        /// <summary>
        /// Sanitizes folder path
        /// </summary>
        private string SanitizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "uploads";

            // Remove any leading/trailing slashes and dots
            path = path.Trim('/', '\\', '.');

            // Replace backslashes with forward slashes
            path = path.Replace('\\', '/');

            // Remove any double slashes
            while (path.Contains("//"))
            {
                path = path.Replace("//", "/");
            }

            // Prevent path traversal
            if (path.Contains(".."))
            {
                path = "uploads";
            }

            return path.ToLowerInvariant();
        }
    }
}