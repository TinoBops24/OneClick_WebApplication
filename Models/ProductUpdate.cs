using Google.Cloud.Firestore;

namespace OneClick_WebApp.Models
{
    // Represents the update/product document timestamp
    [FirestoreData]
    public class ProductUpdate
    {
        [FirestoreProperty]
        public Timestamp Timestamp { get; set; }
    }
}