namespace OneClick_WebApp.Models.ViewModel
{
    public class CartItem
    {
        public string ProductId { get; set; }
        public string ProductName { get; set; }
        public int Quantity { get; set; }
        public double Price { get; set; }
        public string ImageUrl { get; set; }

        public double LineTotal => Quantity * Price;
    }
}
