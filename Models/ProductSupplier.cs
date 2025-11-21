using Google.Cloud.Firestore;

namespace OneClick_WebApp.Models
{
    [FirestoreData]
    public class ProductSupplier
    {
        [FirestoreProperty("ID")]
        public string ID { get; set; }

        [FirestoreProperty("Name")]
        public string Name { get; set; }

        [FirestoreProperty("AccountNo")]
        public string AccountNo { get; set; }

        [FirestoreProperty("BankName")]
        public string BankName { get; set; }

        [FirestoreProperty("Email")]
        public string Email { get; set; }

        [FirestoreProperty("PhoneNumber")]
        public string PhoneNumber { get; set; }

        [FirestoreProperty("ClosingBalance")]
        public double ClosingBalance { get; set; }

        [FirestoreProperty("CreditDays")]
        public int CreditDays { get; set; }

        [FirestoreProperty("Deleted")]
        public bool Deleted { get; set; }

        [FirestoreProperty("HideFromAdmin")]
        public bool HideFromAdmin { get; set; }

        [FirestoreProperty("In")]
        public List<object> In { get; set; }

        [FirestoreProperty("Out")]
        public List<object> Out { get; set; }
    }
}