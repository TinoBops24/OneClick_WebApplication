using FirebaseAdmin.Messaging;
using Google.Cloud.Firestore;
using GroqNet;
using GroqNet.ChatCompletions;
using Microsoft.AspNetCore.Mvc;
using OneClick_WebApp.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;


namespace OneClick_WebApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly FirestoreDb _firestore;
        private readonly ILogger<ChatController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string? _groqApiKey;
        private static readonly string[] Branches = { "almeida", "control", "feprol", "nacala" };

        public ChatController(
            FirestoreDb firestore,
            IConfiguration config,
            ILogger<ChatController> logger,
            IHttpClientFactory httpClientFactory)
        {
            _firestore = firestore;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _groqApiKey = config["Groq:ApiKey"];
        }

        public class ChatRequest
        {
            public string Message { get; set; } = string.Empty;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] ChatRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.Message))
                    return BadRequest(new { reply = "Please type a message." });

                var msg = req.Message.ToLower().Trim();

                // quick greetings/thanks/bye
                if (IsSimpleConversational(msg))
                {
                    var reply = HandleSimpleResponse(msg);
                    return Ok(new { reply = "💊 " + reply });
                }

                // try Groq first
                if (!string.IsNullOrEmpty(_groqApiKey))
                {
                    var aiReply = await TryGroqResponse(req.Message);
                    if (!string.IsNullOrEmpty(aiReply))
                        return Ok(new { reply = "🤖 " + aiReply });
                }

                // fallback
                var fallback = await GetRuleBasedResponse(req.Message);
                return Ok(new { reply = "💊 " + fallback });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing chat");
                return Ok(new { reply = "I'm having some technical difficulties. Please try again later." });
            }
        }

        private async Task<string?> TryGroqResponse(string message)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("Groq");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _groqApiKey);

                var context = await GetPharmacyContext(message);

                var payload = new
                {
                    model = "llama-3.3-70b-versatile",
                    messages = new object[]
                    {
                new { role = "system", content = $@"You are a helpful pharmacy assistant for Commerce Craft Pharmacy.

GUIDELINES:
- Be friendly, concise, and professional
- When users ask about products, provide name, price, and stock
- Always encourage users to purchase online by adding items to their cart on the website
- Do not tell customers to visit branches unless they explicitly ask
- If asked 'where to buy', respond with instructions to add to cart and checkout online
- Keep responses under 150 words
- Use a conversational tone
- If the user confirms with 'yes', 'ok', 'sure', or similar, DO NOT restart the conversation.
- Instead, respond with clear instructions: tell them to add the product to their cart on the website and checkout.
- Never loop back to greetings after confirmation.

CONTEXT:
{context}" },
                new { role = "user", content = message }
                    },
                    temperature = 0.7,
                    top_p = 0.9,
                    max_completion_tokens = 200
                };

                var json = JsonSerializer.Serialize(payload);
                var response = await client.PostAsync(
                    "openai/v1/chat/completions",
                    new StringContent(json, Encoding.UTF8, "application/json")
                );

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Groq API failed: {Status} {Error}", response.StatusCode, error);
                    return null;
                }

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return doc.RootElement
                            .GetProperty("choices")[0]
                            .GetProperty("message")
                            .GetProperty("content")
                            .GetString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Groq API request failed");
                return null;
            }
        }

        // ---------------------------
        // HELPER METHODS
        // ---------------------------

        private bool IsSimpleConversational(string message)
        {
            var simple = new[] { "thanks", "thank you", "bye", "goodbye", "hi", "hello" };
            return simple.Contains(message) || simple.Any(s => message == s + "!");
        }

        private string HandleSimpleResponse(string message)
        {
            if (message.Contains("thank"))
                return "You're welcome! Happy to help. Anything else you need?";

            if (message.Contains("bye") || message.Contains("goodbye"))
                return "Goodbye! Thanks for visiting Commerce Craft Pharmacy. Have a great day!";

            if (message.Contains("hi") || message.Contains("hello"))
                return "Hello! Welcome to Commerce Craft Pharmacy. How can I help you today?";

            return "How can I assist you today?";
        }

        private async Task<string> GetRuleBasedResponse(string message)
        {
            var msg = message.ToLower().Trim();

            if (await IsProductInquiry(msg))
            {
                return await HandleProductInquiry(msg);
            }

            if (IsStoreInfoQuery(msg))
            {
                return await HandleStoreInfo(msg);
            }

            return "I can help you with:\n" +
                    "• Finding medications and health products\n" +
                    "• Checking prices and stock\n" +
                    "• Store information and hours\n" +
                    "• General pharmacy questions\n\n" +
                    "What would you like to know?";
        }

        private async Task<string> GetPharmacyContext(string message)
        {
            try
            {
                var settings = await GetBusinessSettings();
                var companyName = settings.GetValueOrDefault("companyName", "Commerce Craft Pharmacy");
                var description = settings.GetValueOrDefault("description", "Trusted pharmacy");
                var phone = settings.GetValueOrDefault("contactPagePhone", "Contact us");
                var location = settings.GetValueOrDefault("contactPageLocation", "Multiple locations");

                var products = await GetProducts();
                var stockData = await LoadStockByProductAsync();

                var matchedProduct = products.FirstOrDefault(p =>
                    message.ToLower().Contains(p.Name.ToLower()) ||
                    p.Name.ToLower().Contains(message.ToLower()));

                if (matchedProduct != null)
                {
                    stockData.TryGetValue(matchedProduct.Id, out double stock);
                    return $@"
SPECIFIC PRODUCT INFO:
- Product: {matchedProduct.Name}
- Price: R{matchedProduct.Price:F2}
- Stock: {stock} units available
- Description: {matchedProduct.Description}

PHARMACY INFO:
- Name: {companyName}
- Description: {description}
- Phone: {phone}
- Location: {location}
- Branches: Almeida, Control, Feprol, Nacala";
                }

                return $@"
PHARMACY INFO:
- Name: {companyName}
- Description: {description}
- Phone: {phone}
- Location: {location}
- Branches: Almeida, Control, Feprol, Nacala
- How to Buy: Customers can add products directly to their cart on our website and checkout securely online.
- Orders can be prepared for pickup or delivery (if available).";
            }
            catch
            {
                return "We are a trusted pharmacy providing quality medications and health products.";
            }
        }

        private async Task<bool> IsProductInquiry(string message)
        {
            var productKeywords = new[] { "price", "cost", "stock", "available", "sell", "have", "medication", "medicine" };
            if (productKeywords.Any(k => message.Contains(k)))
                return true;

            try
            {
                var products = await GetProducts();
                return products.Any(p =>
                    message.Contains(p.Name.ToLower()) ||
                    p.Name.ToLower().Contains(message));
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> HandleProductInquiry(string message)
        {
            try
            {
                var products = await GetProducts();
                var stockData = await LoadStockByProductAsync();

                var matches = products.Where(p =>
                    p.Name.ToLower().Contains(message) ||
                    message.Contains(p.Name.ToLower())
                ).Take(3).ToList();

                if (matches.Any())
                {
                    var results = matches.Select(p =>
                    {
                        stockData.TryGetValue(p.Id, out double stock);
                        var status = stock > 0 ? $"✅ {stock} in stock" : "❌ Out of stock";
                        return $"{status} **{p.Name}** - R{p.Price:F2}";
                    });

                    return string.Join("\n", results) + "\n\nNeed more details about any product?";
                }

                return $"Sorry, couldn't find that product. We have {products.Count} items available. Try a different search term or contact us directly.";
            }
            catch
            {
                return "Having trouble accessing product info. Please contact our pharmacy directly.";
            }
        }

        private bool IsStoreInfoQuery(string message)
        {
            var storeKeywords = new[] { "hours", "location", "address", "phone", "contact" };
            return storeKeywords.Any(k => message.Contains(k));
        }

        private async Task<string> HandleStoreInfo(string message)
        {
            var settings = await GetBusinessSettings();

            if (message.Contains("hours"))
            {
                return "🕐 Store Hours:\nMon-Fri: 8AM-6PM\nSat: 8AM-4PM\nSun: 9AM-2PM";
            }

            if (message.Contains("location") || message.Contains("address"))
            {
                var location = settings.GetValueOrDefault("contactPageLocation", "Multiple locations available");
                return $"📍 Location: {location}\n\nBranches: Almeida, Control, Feprol, Nacala";
            }

            if (message.Contains("phone") || message.Contains("contact"))
            {
                var phone = settings.GetValueOrDefault("contactPagePhone", "Contact info not available");
                return $"📞 Phone: {phone}\nWe're here to help with all your pharmacy needs!";
            }

            return "We have multiple locations and are here to serve you. Contact us for specific branch information.";
        }

        // ---------------------------
        // FIRESTORE HELPERS
        // ---------------------------

        private async Task<Dictionary<string, string>> GetBusinessSettings()
        {
            try
            {
                var settingsDoc = await _firestore.Collection("settings").Document("almeida").GetSnapshotAsync();
                if (settingsDoc.Exists)
                {
                    return settingsDoc.ToDictionary().ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value?.ToString() ?? ""
                    );
                }
                return new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting business settings");
                return new Dictionary<string, string>();
            }
        }

        private async Task<List<Product>> GetProducts()
        {
            try
            {
                var snapshot = await _firestore.Collection("product").GetSnapshotAsync();
                return snapshot.Documents
                    .Where(d => d.Exists)
                    .Select(d => d.ConvertTo<Product>())
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting products");
                return new List<Product>();
            }
        }

        private async Task<Dictionary<string, double>> LoadStockByProductAsync()
        {
            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var branch in Branches)
            {
                try
                {
                    var stockSnap = await _firestore.Collection("location").Document(branch).Collection("stock").GetSnapshotAsync();
                    foreach (var sdoc in stockSnap.Documents)
                    {
                        double qty = 0;
                        if (sdoc.TryGetValue<double>("Quantity", out var qDouble)) qty = qDouble;
                        else if (sdoc.TryGetValue<long>("Quantity", out var qLong)) qty = qLong;

                        dict[sdoc.Id] = dict.TryGetValue(sdoc.Id, out var cur) ? cur + qty : qty;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load stock for branch: {Branch}", branch);
                }
            }

            return dict;
        }
    }
}
    
