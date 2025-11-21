using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace OneClick_WebApp.Helpers
{
    public static class SessionExtensions
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static void SetObject<T>(this ISession session, string key, T value)
        {
            if (value == null)
            {
                session.Remove(key);
                return;
            }
            var jsonString = JsonSerializer.Serialize(value, _options);
            session.SetString(key, jsonString);
        }

        public static T GetObject<T>(this ISession session, string key)
        {
            var jsonString = session.GetString(key);
            if (string.IsNullOrEmpty(jsonString))
                return default;

            try
            {
                return JsonSerializer.Deserialize<T>(jsonString, _options);
            }
            catch (JsonException)
            {
                // Optionally log or handle deserialization error
                return default;
            }
        }
    }
}
