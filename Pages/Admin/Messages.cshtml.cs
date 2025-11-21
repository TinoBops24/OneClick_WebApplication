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
    public class MessagesModel : BasePageModel
    {
        private readonly ILogger<MessagesModel> _logger;

        public MessagesModel(FirebaseDbService dbService, ILogger<MessagesModel> logger) : base(dbService)
        {
            _logger = logger;
        }

        public List<ContactMessage> Messages { get; set; } = new();
        public int TotalCount { get; set; }
        public int UnreadCount { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

        // Filter properties
        [BindProperty(SupportsGet = true)]
        public string SearchTerm { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool? UnreadOnly { get; set; }

        [BindProperty(SupportsGet = true)]
        public int Page { get; set; } = 1;

        [TempData]
        public string SuccessMessage { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public async Task OnGetAsync()
        {
            await LoadSiteSettingsAsync();
            CurrentPage = Page;
            await LoadMessagesAsync();
        }

        private async Task LoadMessagesAsync()
        {
            try
            {
                // Get all messages first
                var allMessages = await _dbService.GetAllDocumentsAsync<ContactMessage>("messages");

                // Convert to IEnumerable to work with in-memory data
                var filteredMessages = allMessages.AsEnumerable();

                // Count total unread messages
                UnreadCount = allMessages.Count(m => !m.IsRead);

                // Apply search filter
                if (!string.IsNullOrWhiteSpace(SearchTerm))
                {
                    filteredMessages = filteredMessages.Where(m =>
                        (!string.IsNullOrEmpty(m.Name) && m.Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(m.Email) && m.Email.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(m.Subject) && m.Subject.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(m.Message) && m.Message.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)));
                }

                // Apply read/unread filter
                if (UnreadOnly.HasValue)
                {
                    filteredMessages = filteredMessages.Where(m => !m.IsRead == UnreadOnly.Value);
                }

                // Get total count for pagination
                TotalCount = filteredMessages.Count();

                // Apply pagination and sorting
                Messages = filteredMessages
                    .OrderByDescending(m => m.Timestamp)
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .ToList();

                _logger.LogInformation("Loaded {MessageCount} messages (page {CurrentPage} of {TotalPages})",
                    Messages.Count, CurrentPage, TotalPages);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load messages");
                Messages = new List<ContactMessage>();
                TotalCount = 0;
                UnreadCount = 0;
            }
        }

        public async Task<IActionResult> OnPostMarkAsReadAsync(string messageId)
        {
            try
            {
                await _dbService.MarkMessageAsReadAsync(messageId);
                SuccessMessage = "Message marked as read.";
                _logger.LogInformation("Message {MessageId} marked as read", messageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark message as read: {MessageId}", messageId);
                ErrorMessage = "Failed to mark message as read. Please try again.";
            }

            return RedirectToPage(new { Page = CurrentPage, SearchTerm, UnreadOnly });
        }

        public async Task<IActionResult> OnPostMarkAllReadAsync()
        {
            try
            {
                var unreadMessages = await _dbService.GetAllMessagesAsync(unreadOnly: true);

                foreach (var message in unreadMessages)
                {
                    await _dbService.MarkMessageAsReadAsync(message.Id);
                }

                SuccessMessage = $"Marked {unreadMessages.Count} messages as read.";
                _logger.LogInformation("Marked {Count} messages as read", unreadMessages.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark all messages as read");
                ErrorMessage = "Failed to mark messages as read. Please try again.";
            }

            return RedirectToPage(new { Page = CurrentPage, SearchTerm, UnreadOnly });
        }
    }
}