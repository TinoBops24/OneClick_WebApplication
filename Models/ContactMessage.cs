using Google.Cloud.Firestore;
using System.ComponentModel.DataAnnotations;

namespace OneClick_WebApp.Models
{
    [FirestoreData]
    public class ContactMessage
    {
        [FirestoreDocumentId]
        public string Id { get; set; }

        [FirestoreProperty("name")]
        [Required]
        public string Name { get; set; }

        [FirestoreProperty("email")]
        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [FirestoreProperty("subject")]
        [Required]
        public string Subject { get; set; }

        [FirestoreProperty("message")]
        [Required]
        public string Message { get; set; }

        [FirestoreProperty("timestamp")]
        public Timestamp? Timestamp { get; set; }

        [FirestoreProperty("isRead")]
        public bool IsRead { get; set; } = false;

        /// <summary>
        /// Defines the scope/audience for this message.
        /// AdminOnly: Only visible in admin panel (contact forms, system notifications)
        /// Customer: Only visible to specific customer (order updates, account notifications)
        /// Global: Visible to all authenticated users (promotions, announcements)
        /// </summary>
        [FirestoreProperty("messageScope")]
        public string MessageScope { get; set; } = "AdminOnly";

        /// <summary>
        /// For customer-scoped messages, specifies which user should see it (email address)
        /// </summary>
        [FirestoreProperty("targetUserId")]
        public string TargetUserId { get; set; }

        /// <summary>
        /// Category for better organisation (ContactForm, SystemAlert, OrderUpdate, etc.)
        /// </summary>
        [FirestoreProperty("messageCategory")]
        public string MessageCategory { get; set; } = "ContactForm";
    }

    /// <summary>
    /// Enum-like constants for message scopes to maintain consistency
    /// </summary>
    public static class MessageScope
    {
        public const string AdminOnly = "AdminOnly";
        public const string Customer = "Customer";
        public const string Global = "Global";
    }

    /// <summary>
    /// Enum-like constants for message categories
    /// </summary>
    public static class MessageCategory
    {
        public const string ContactForm = "ContactForm";
        public const string SystemAlert = "SystemAlert";
        public const string OrderUpdate = "OrderUpdate";
        public const string AccountNotification = "AccountNotification";
        public const string ConfigChange = "ConfigChange";
    }
}