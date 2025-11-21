using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using OneClick_WebApp.Models;
using Google.Cloud.Firestore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OneClick_WebApp.Services
{
    public class CacheManagerService
    {
        private readonly IMemoryCache _cache;
        private readonly FirebaseDbService _dbService;
        private readonly ILogger<CacheManagerService> _logger;

        // Cache keys
        private const string ProductsCacheKey = "app_products_cache";
        private const string SiteSettingsCacheKey = "app_site_settings_cache";
        private const string LastUpdateTimestampKey = "app_last_update_timestamp";

        // Firestore paths
        private const string UpdateCollection = "update";
        private const string ProductUpdateDocument = "product";

        public CacheManagerService(
            IMemoryCache cache,
            FirebaseDbService dbService,
            ILogger<CacheManagerService> logger)
        {
            _cache = cache;
            _dbService = dbService;
            _logger = logger;
        }

        // Get products from cache or load if not cached
        public async Task<List<Product>> GetProductsAsync()
        {
            if (_cache.TryGetValue(ProductsCacheKey, out List<Product> cachedProducts))
            {
                _logger.LogDebug("Products loaded from cache ({Count} items)", cachedProducts.Count);
                return cachedProducts;
            }

            return await RefreshProductsCacheAsync();
        }

        // Force refresh products from database
        public async Task<List<Product>> RefreshProductsCacheAsync()
        {
            _logger.LogInformation("Refreshing products cache from database");

            var products = await _dbService.GetAllProductsAsync();

            // Cache indefinitely until manually refreshed
            _cache.Set(ProductsCacheKey, products, new MemoryCacheEntryOptions
            {
                Priority = CacheItemPriority.High
            });

            // Store the current update timestamp
            var currentTimestamp = await GetProductUpdateTimestampAsync();
            if (currentTimestamp.HasValue)
            {
                _cache.Set(LastUpdateTimestampKey, currentTimestamp.Value, new MemoryCacheEntryOptions
                {
                    Priority = CacheItemPriority.High
                });
            }

            _logger.LogInformation("Products cache refreshed ({Count} products loaded)", products.Count);
            return products;
        }

        // Get site settings from cache or load if not cached
        public async Task<Branch> GetSiteSettingsAsync()
        {
            if (_cache.TryGetValue(SiteSettingsCacheKey, out Branch cachedSettings))
            {
                _logger.LogDebug("Site settings loaded from cache");
                return cachedSettings;
            }

            return await RefreshSiteSettingsCacheAsync();
        }

        // Force refresh site settings from database
        public async Task<Branch> RefreshSiteSettingsCacheAsync()
        {
            _logger.LogInformation("Refreshing site settings cache from database");

            var settings = await _dbService.GetBranchConfigurationAsync();

            // Cache indefinitely until manually refreshed
            _cache.Set(SiteSettingsCacheKey, settings, new MemoryCacheEntryOptions
            {
                Priority = CacheItemPriority.High
            });

            _logger.LogInformation("Site settings cache refreshed");
            return settings;
        }

        // Check if products need updating by comparing timestamps
        public async Task<bool> CheckAndRefreshIfNeededAsync()
        {
            try
            {
                var currentTimestamp = await GetProductUpdateTimestampAsync();
                if (!currentTimestamp.HasValue)
                {
                    _logger.LogWarning("Could not retrieve product update timestamp from database");
                    return false;
                }

                // Get last known timestamp from cache
                if (_cache.TryGetValue(LastUpdateTimestampKey, out Timestamp cachedTimestamp))
                {
                    // Compare timestamps
                    if (currentTimestamp.Value.ToDateTime() > cachedTimestamp.ToDateTime())
                    {
                        _logger.LogInformation(
                            "Product update detected: cached={CachedTime}, current={CurrentTime}",
                            cachedTimestamp.ToDateTime(),
                            currentTimestamp.Value.ToDateTime());

                        await RefreshProductsCacheAsync();
                        return true;
                    }
                    else
                    {
                        _logger.LogDebug("No product updates detected");
                        return false;
                    }
                }
                else
                {
                    // No cached timestamp, refresh products
                    _logger.LogInformation("No cached timestamp found, refreshing products");
                    await RefreshProductsCacheAsync();
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for product updates");
                return false;
            }
        }

        // Get the current product update timestamp from Firestore
        private async Task<Timestamp?> GetProductUpdateTimestampAsync()
        {
            try
            {
                var updateDoc = await _dbService.GetDocumentAsync<ProductUpdate>(
                    UpdateCollection,
                    ProductUpdateDocument);

                return updateDoc?.Timestamp;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching product update timestamp");
                return null;
            }
        }

        // Manually flush specific cache
        public void FlushCache(string category)
        {
            switch (category.ToLower())
            {
                case "products":
                    _cache.Remove(ProductsCacheKey);
                    _cache.Remove(LastUpdateTimestampKey);
                    _logger.LogInformation("Products cache flushed manually");
                    break;

                case "settings":
                    _cache.Remove(SiteSettingsCacheKey);
                    _logger.LogInformation("Site settings cache flushed manually");
                    break;

                case "all":
                    _cache.Remove(ProductsCacheKey);
                    _cache.Remove(SiteSettingsCacheKey);
                    _cache.Remove(LastUpdateTimestampKey);
                    _logger.LogInformation("All caches flushed manually");
                    break;

                default:
                    _logger.LogWarning("Unknown cache category: {Category}", category);
                    break;
            }
        }

        // Get cache statistics
        public CacheStatistics GetCacheStatistics()
        {
            var productsCached = _cache.TryGetValue(ProductsCacheKey, out List<Product> products);
            var settingsCached = _cache.TryGetValue(SiteSettingsCacheKey, out Branch settings);

            DateTime? lastUpdate = null;
            if (_cache.TryGetValue(LastUpdateTimestampKey, out Timestamp timestamp))
            {
                lastUpdate = timestamp.ToDateTime();
            }

            return new CacheStatistics
            {
                ProductsCached = productsCached,
                ProductCount = products?.Count ?? 0,
                SiteSettingsCached = settingsCached,
                LastUpdateTimestamp = lastUpdate
            };
        }
    }

    // Cache statistics model
    public class CacheStatistics
    {
        public bool ProductsCached { get; set; }
        public int ProductCount { get; set; }
        public bool SiteSettingsCached { get; set; }
        public DateTime? LastUpdateTimestamp { get; set; }
    }
}