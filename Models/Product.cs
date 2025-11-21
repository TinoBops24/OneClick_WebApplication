using Google.Cloud.Firestore;

namespace OneClick_WebApp.Models
{
    [FirestoreData]
    public class Product
    {
        [FirestoreDocumentId]
        public string DocumentId { get; set; }

        [FirestoreProperty("ID")]
        public string Id { get; set; }

        [FirestoreProperty]
        public string SKU { get; set; }

        [FirestoreProperty("Barcode")]
        public string Barcode { get; set; }

        [FirestoreProperty("Name")]
        public string Name { get; set; }

        [FirestoreProperty("Description")]
        public string Description { get; set; }

        [FirestoreProperty("category")]
        public ProductCategory Category { get; set; }

        [FirestoreProperty("CostPrice")]
        public double CostPrice { get; set; }

        [FirestoreProperty("Price")]
        public double Price { get; set; }

        [FirestoreProperty("NacalaPrice")]
        public double NacalaPrice { get; set; }

        [FirestoreProperty("LotNumber")]
        public string LotNumber { get; set; }

        [FirestoreProperty("ExpiryDate")]
        public Timestamp? ExpiryDate { get; set; }

        [FirestoreProperty("IVA")]
        public bool IVA { get; set; }

        [FirestoreProperty("Order")]
        public int Order { get; set; }

        [FirestoreProperty("CommentForProduct")]
        public string CommentForProduct { get; set; }

        [FirestoreProperty("Grams")]
        public bool Grams { get; set; }

        [FirestoreProperty("HideInPOS")]
        public bool HideInPOS { get; set; }

        [FirestoreProperty("HideInWeb")]
        public bool HideInWeb { get; set; }

        [FirestoreProperty("PictureAttachment")]
        public string ImageUrl { get; set; }

        [FirestoreProperty("IVAAmount")]
        public double IVAAmount { get; set; }

        [FirestoreProperty("IVAPercentage")]
        public double IVAPercentage { get; set; }

        [FirestoreProperty("PreviousCostPrice")]
        public double PreviousCostPrice { get; set; }

        [FirestoreProperty("PriceWithoutIVA")]
        public double PriceWithoutIVA { get; set; }

        [FirestoreProperty("ProduceOnSale")]
        public bool ProduceOnSale { get; set; }

        [FirestoreProperty("ProductType")]
        public int ProductType { get; set; }

        [FirestoreProperty("supplier")]
        public ProductSupplier Supplier { get; set; }
        [FirestoreProperty("StockQuantity")]
        public int StockQuantity { get; set; }
    }
}