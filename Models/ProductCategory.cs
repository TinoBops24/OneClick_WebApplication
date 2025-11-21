using Google.Cloud.Firestore;

namespace OneClick_WebApp.Models
{
    [FirestoreData]
    public class ProductCategory
    {
        [FirestoreProperty("ID")]
        public string Id { get; set; }

        [FirestoreProperty("Name")]
        public string Name { get; set; }
    }
}