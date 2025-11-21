using Google.Cloud.Firestore;
using System.ComponentModel.DataAnnotations;

namespace OneClick_WebApp.Models
{
    [FirestoreData]
    public class StockMovement
    {
   
        [FirestoreProperty("timestamp")]
        public DateTime? Timestamp { get; set; }

        [FirestoreProperty("product")]
        public Product Product { get; set; } 

        [FirestoreProperty("quantity")]
        public int Quantity { get; set; }

        [FirestoreProperty("lineTotal")]
        public double LineTotal { get; set; }

        [FirestoreProperty("ivaTotal")]
        public double IVATotal { get; set; }

        [FirestoreProperty("lineTotalWithoutIVA")]
        public double LineTotalWithoutIVA { get; set; }

        [FirestoreProperty("forWho")]
        public string ForWho { get; set; }

        [FirestoreProperty("salesRep")]
        public string SalesRep { get; set; }

        [FirestoreProperty("printed")]
        public bool Printed { get; set; }

        [FirestoreProperty("paid")]
        public bool Paid { get; set; }

        [FirestoreProperty("selected")]
        public bool Selected { get; set; }

        [FirestoreProperty("discountPercentage")]
        public double DiscountPercentage { get; set; }

        [FirestoreProperty("discountAmount")]
        public double DiscountAmount { get; set; }

        [FirestoreProperty("discountPrice")]
        public double DiscountPrice { get; set; }

        [FirestoreProperty("production")]
        public bool Production { get; set; }

        [FirestoreProperty("pumpID")]
        public string PumpID { get; set; }

        [FirestoreProperty("pumpGun")]
        public PumpGun PumpGun { get; set; }

        [FirestoreProperty("expectedStock")]
        public int ExpectedStock { get; set; }

        [FirestoreProperty("differenceInStock")]
        public int DifferenceInStock { get; set; }

        [FirestoreProperty("currentStockCount")]
        public int CurrentStockCount { get; set; }

        // === LEGACY/COMPATIBILITY FIELDS ===
        // Keep these for backward compatibility with web app
        [FirestoreProperty("productId")]
        public string ProductId { get; set; }

        [FirestoreProperty("productName")]
        public string ProductName { get; set; }

        [FirestoreProperty("unitPrice")]
        public double UnitPrice { get; set; }

        [FirestoreProperty("imageUrl")]
        public string ImageUrl { get; set; }

        [FirestoreProperty("category")]
        public string Category { get; set; }

        [FirestoreProperty("sku")]
        public string SKU { get; set; }

        // === HELPER METHODS ===
        /// <summary>
        /// Populate all fields from Product object for POS compatibility
        /// Call this method after setting the Product property
        /// </summary>
        public void PopulateFromProduct()
        {
            if (Product != null)
            {
                ProductId = Product.DocumentId;
                ProductName = Product.Name;
                UnitPrice = Product.Price;
                ImageUrl = Product.ImageUrl;
                Category = Product.Category?.Name;
                SKU = Product.SKU;
            }
        }

        /// <summary>
        /// Initialize with default POS values for new transactions
        /// </summary>
        public void InitializeForPOS(string salesRep = "Web Order", string forWho = "Customer")
        {
            Timestamp = DateTime.Now;
            ForWho = forWho;
            SalesRep = salesRep;
            Printed = false;
            Paid = false;
            Selected = false;
            Production = false;
            DiscountPercentage = 0.0;
            DiscountAmount = 0.0;
            DiscountPrice = UnitPrice;
            ExpectedStock = 0;
            DifferenceInStock = 0;
            CurrentStockCount = 0;
        }

        public override string ToString()
        {
            return $"{Product?.Name ?? ProductName} - {Product?.Description ?? ""} x {Quantity} @ {UnitPrice:C} = {LineTotal:C}";
        }
    }
}