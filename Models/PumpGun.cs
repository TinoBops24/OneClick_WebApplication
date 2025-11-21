using Google.Cloud.Firestore;

namespace OneClick_WebApp.Models
{
    [FirestoreData]
    public class PumpGun
    {
        [FirestoreProperty("id")]
        public string Id { get; set; }

        [FirestoreProperty("pumpName")]
        public string PumpName { get; set; }

        [FirestoreProperty("gunNumber")]
        public string GunNumber { get; set; }

        [FirestoreProperty("isActive")]
        public bool IsActive { get; set; }

        public PumpGun()
        {
            IsActive = true;
        }

        // Helper property for backward compatibility if needed
        public string Name => $"{PumpName} - Gun {GunNumber}";
    }
}