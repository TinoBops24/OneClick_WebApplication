using Google.Cloud.Firestore;

namespace OneClick_WebApp.Models
{
    [FirestoreData]
    public class Category
    {
        [FirestoreDocumentId] // This attribute maps the document ID to this property
        public string Id { get; set; }

        [FirestoreProperty("name")]
        public string Name { get; set; }
    }
}