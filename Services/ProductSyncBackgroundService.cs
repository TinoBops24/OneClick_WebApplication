using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OneClick_WebApp.Services
{
    public class ProductSyncBackgroundService : BackgroundService
    {
        private readonly ILogger<ProductSyncBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _checkInterval;

        public ProductSyncBackgroundService(
            ILogger<ProductSyncBackgroundService> logger,
            IServiceProvider serviceProvider,
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;

            // Read interval from config, default to 5 minutes
            var intervalMinutes = configuration.GetValue<int>("ProductSync:IntervalMinutes", 5);
            _checkInterval = TimeSpan.FromMinutes(intervalMinutes);

            _logger.LogInformation(
                "Product sync service initialised with {IntervalMinutes} minute check interval",
                intervalMinutes);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Product sync background service started");

            // Wait a bit before first check to let app initialise
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckForProductUpdatesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in product sync background service");
                }

                // Wait for next check interval
                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("Product sync background service stopped");
        }

        private async Task CheckForProductUpdatesAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var cacheManager = scope.ServiceProvider.GetRequiredService<CacheManagerService>();

            _logger.LogDebug("Checking for product updates");

            var wasUpdated = await cacheManager.CheckAndRefreshIfNeededAsync();

            if (wasUpdated)
            {
                _logger.LogInformation("Products cache automatically updated by background service");
            }
        }
    }
}