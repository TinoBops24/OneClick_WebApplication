using System.Text.Json;
using OneClick_WebApp.Models.ViewModel;

namespace OneClick_WebApp.Services
{
    public class CartService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private const string CartSessionKey = "ShoppingCart";

        public CartService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        private ISession Session => _httpContextAccessor.HttpContext.Session;

        public List<CartItem> GetCartItems()
        {
            var cartJson = Session.GetString(CartSessionKey);
            if (string.IsNullOrEmpty(cartJson))
            {
                return new List<CartItem>();
            }
            return JsonSerializer.Deserialize<List<CartItem>>(cartJson);
        }

        public void SaveCartItems(List<CartItem> cartItems)
        {
            var cartJson = JsonSerializer.Serialize(cartItems);
            Session.SetString(CartSessionKey, cartJson);
        }

        public void AddToCart(string productId, string productName, double price, string imageUrl)
        {
            var cart = GetCartItems();
            var existingItem = cart.FirstOrDefault(item => item.ProductId == productId);

            if (existingItem != null)
            {
                existingItem.Quantity++;
            }
            else
            {
                cart.Add(new CartItem
                {
                    ProductId = productId,
                    ProductName = productName,
                    Price = price,
                    Quantity = 1,
                    ImageUrl = imageUrl
                });
            }
            SaveCartItems(cart);
        }

        public void UpdateQuantity(string productId, int quantity)
        {
            var cart = GetCartItems();
            var itemToUpdate = cart.FirstOrDefault(item => item.ProductId == productId);
            if (itemToUpdate != null)
            {
                if (quantity > 0)
                {
                    itemToUpdate.Quantity = quantity;
                }
                else
                {
                    // Remove if quantity is 0 or less
                    cart.Remove(itemToUpdate);
                }
            }
            SaveCartItems(cart);
        }

        public void RemoveFromCart(string productId)
        {
            var cart = GetCartItems();
            var itemToRemove = cart.FirstOrDefault(item => item.ProductId == productId);
            if (itemToRemove != null)
            {
                cart.Remove(itemToRemove);
            }
            SaveCartItems(cart);
        }

        public void ClearCart()
        {
            Session.Remove(CartSessionKey);
        }

        public double GetGrandTotal()
        {
            return GetCartItems().Sum(item => item.LineTotal);
        }
    }
}