using Google.Cloud.Firestore;
using OneClick_WebApp.Models.Enums;
using System.Collections.Generic;

namespace OneClick_WebApp.Models
{
    [FirestoreData]
    public class UserAccount
    {
        
        [FirestoreDocumentId]
        public string Id { get; set; }

        
        [FirestoreProperty("firebaseUid")]
        public string FirebaseUid { get; set; }

        [FirestoreProperty("email")]
        public string Email { get; set; }

        [FirestoreProperty("name")]
        public string Name { get; set; }

        [FirestoreProperty("branchAccess")]
        public Dictionary<string, bool> BranchAccess { get; set; } = new();

        [FirestoreProperty("userRole")]
        public Role UserRole { get; set; } = Role.User; 

        [FirestoreProperty("disabled")]
        public bool Disabled { get; set; } = false;

        [FirestoreProperty("code")]
        public string Code { get; set; }

        [FirestoreProperty("imageUrl")]
        public string ImageUrl { get; set; }

        [FirestoreProperty("isErpUser")]
        public bool IsErpUser { get; set; } = false;

        
        public string DisplayId => Id;

        
    }
}