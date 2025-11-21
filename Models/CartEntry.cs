using OneClick_WebApp.Models;

namespace OneClick_WebApp.Models
{
    /// <summary>
    /// Represents a cart entry for POS integration processing
    /// Bridges the gap between web cart items and POS stock movements
    /// </summary>
    public class CartEntry
    {
        
        /// The complete product object (required by POS system)
        
        public Product Product { get; set; }

        
        /// Quantity of this product in the cart
        
        public int Quantity { get; set; }

        
        /// Calculate line total for this cart entry
        
        public double LineTotal => Product?.Price * Quantity ?? 0.0;

        
        /// Calculate IVA total for this cart entry
        
        public double IVATotal => Product?.IVA == true ? (Product.IVAAmount * Quantity) : 0.0;

       
        /// Calculate line total without IVA
        
        public double LineTotalWithoutIVA => Product?.IVA == true ? (Product.PriceWithoutIVA * Quantity) : LineTotal;

        
        /// Create a properly formatted stock movement for this cart entry
       
        public StockMovement CreateStockMovement(string forWho = "Customer", string salesRep = "Web Order")
        {
            var stockMovement = new StockMovement
            {
                Product = this.Product,
                Quantity = this.Quantity,
                UnitPrice = Product?.Price ?? 0,
                LineTotal = this.LineTotal,
                IVATotal = this.IVATotal,
                LineTotalWithoutIVA = this.LineTotalWithoutIVA,
                ForWho = forWho,
                SalesRep = salesRep
            };

            // Initialize POS-required fields
            stockMovement.InitializeForPOS(salesRep, forWho);

            // Populate legacy fields for compatibility
            stockMovement.PopulateFromProduct();

            return stockMovement;
        }
    }
}