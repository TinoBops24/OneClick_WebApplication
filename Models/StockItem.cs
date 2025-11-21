using Google.Cloud.Firestore;
using System.ComponentModel.DataAnnotations;

namespace OneClick_WebApp.Models
{
    [FirestoreData]
    public class StockItem
    {
        /// <summary>
        /// The complete product object (required by POS system)
        /// </summary>
        [FirestoreProperty("product")]
        [Required]
        public Product Product { get; set; }

        /// <summary>
        /// Current quantity in stock
        /// </summary>
        [FirestoreProperty("quantity")]
        [Required]
        public int Quantity { get; set; }

        /// <summary>
        /// List of incoming stock movements (purchases, returns, adjustments in)
        /// </summary>
        [FirestoreProperty("in")]
        [Required]
        public List<StockMovement> In { get; set; }

        /// <summary>
        /// List of outgoing stock movements (sales, wastage, adjustments out)
        /// </summary>
        [FirestoreProperty("out")]
        [Required]
        public List<StockMovement> Out { get; set; }

        /// <summary>
        /// Running total of all stock counts
        /// </summary>
        [FirestoreProperty("accumulatedStockCounts")]
        public int AccumulatedStockCounts { get; set; }

        /// <summary>
        /// History of stock count adjustments
        /// </summary>
        [FirestoreProperty("stockCounts")]
        public List<StockMovement> StockCounts { get; set; }

        // === LEGACY/WEB APP COMPATIBILITY ===
        /// <summary>
        /// Product ID for document identification (backward compatibility)
        /// </summary>
        [FirestoreDocumentId]
        public string ProductId { get; set; }

        /// <summary>
        /// Constructor for new stock items
        /// </summary>
        public StockItem()
        {
            In = new List<StockMovement>();
            Out = new List<StockMovement>();
            StockCounts = new List<StockMovement>();
            AccumulatedStockCounts = 0;
        }

        /// <summary>
        /// Initialize stock item with product
        /// </summary>
        public StockItem(Product product, int initialQuantity = 0) : this()
        {
            Product = product;
            ProductId = product?.DocumentId;
            Quantity = initialQuantity;
        }

        /// <summary>
        /// Add incoming stock (purchase, return, adjustment in)
        /// </summary>
        public void AddIncomingStock(StockMovement movement)
        {
            if (movement == null) throw new ArgumentNullException(nameof(movement));

            In.Add(movement);
            Quantity += movement.Quantity;
        }

        /// <summary>
        /// Add outgoing stock (sale, wastage, adjustment out)
        /// </summary>
        public void AddOutgoingStock(StockMovement movement)
        {
            if (movement == null) throw new ArgumentNullException(nameof(movement));

            Out.Add(movement);
            Quantity -= movement.Quantity;

            // Prevent negative stock
            if (Quantity < 0) Quantity = 0;
        }

        /// <summary>
        /// Add stock count adjustment
        /// </summary>
        public void AddStockCount(StockMovement stockCount)
        {
            if (stockCount == null) throw new ArgumentNullException(nameof(stockCount));

            StockCounts.Add(stockCount);
            AccumulatedStockCounts += stockCount.Quantity;

            // Update current quantity based on stock count difference
            Quantity += stockCount.DifferenceInStock;
        }

        /// <summary>
        /// Get total quantity sold
        /// </summary>
        public int GetTotalSold()
        {
            return Out?.Where(m => !m.Production).Sum(m => m.Quantity) ?? 0;
        }

        /// <summary>
        /// Get total quantity purchased
        /// </summary>
        public int GetTotalPurchased()
        {
            return In?.Sum(m => m.Quantity) ?? 0;
        }

        /// <summary>
        /// Calculate current stock value
        /// </summary>
        public double GetStockValue()
        {
            return Quantity * (Product?.Price ?? 0);
        }

        /// <summary>
        /// Create a sale stock movement for this item
        /// </summary>
        public StockMovement CreateSaleMovement(int quantitySold, string salesRep = "Web Order", string forWho = "Customer")
        {
            var movement = new StockMovement
            {
                Product = this.Product,
                Quantity = quantitySold,
                UnitPrice = Product?.Price ?? 0,
                LineTotal = quantitySold * (Product?.Price ?? 0)
            };

            movement.PopulateFromProduct();
            movement.InitializeForPOS(salesRep, forWho);

            // Calculate IVA if applicable
            if (Product != null && Product.IVA)
            {
                movement.IVATotal = Product.IVAAmount * quantitySold;
                movement.LineTotalWithoutIVA = Product.PriceWithoutIVA * quantitySold;
            }
            else
            {
                movement.IVATotal = 0;
                movement.LineTotalWithoutIVA = movement.LineTotal;
            }

            return movement;
        }
    }
}