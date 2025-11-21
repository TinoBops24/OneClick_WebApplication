using Google.Cloud.Firestore;
using OneClick_WebApp.Models;
using System.ComponentModel.DataAnnotations;

namespace OneClick_WebApp
{

    [FirestoreData]
    public class Comment
    {

        [FirestoreProperty]
        public string Author { get; set; }

        [FirestoreProperty]
        public string Message { get; set; }

        [FirestoreProperty]
        public Timestamp CreatedAt { get; set; }
    }
}
