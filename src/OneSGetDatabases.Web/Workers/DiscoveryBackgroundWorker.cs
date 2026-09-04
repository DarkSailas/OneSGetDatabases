using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Web.Workers;

public class DiscoveryBackgroundWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDatabaseCacheService _cache;
    private readonly ILogger<DiscoveryBackgroundWorker> _logger;
    private readonly SchedulerConfig _config;

    private DateTime _lastConfluenceSyncDate = DateTime.MinValue.Date;
    private DateTime _lastConfluenceSyncTime = DateTime.MinValue;

    public DiscoveryBackgroundWorker(
        IServiceProvider serviceProvider,
        IDatabaseCacheService cache,
        IOptions<SchedulerConfig> config,
        ILogger<DiscoveryBackgroundWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _cache = cache;
        _logger = logger;
        _config = config.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DiscoveryBackgroundWorker started. Discovery interval: {Minutes}m, AutoSyncConfluence: {AutoSync}, Schedule: {Time}",
            _config.DiscoveryIntervalMinutes, _config.EnableAutoSyncConfluence, _config.ConfluenceSyncTime);

        // Initial delay to allow Kestrel web server to bind ports smoothly
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Executing background 1C database discovery...");
                using var scope = _serviceProvider.CreateScope();
                var discoveryService = scope.ServiceProvider.GetRequiredService<IInfobaseDiscoveryService>();

                var bases = await discoveryService.DiscoverAllAsync(stoppingToken);
                _cache.Update(bases);
                _logger.LogInformation("Background discovery completed. {Count} bases updated in cache", bases.Count);

                // Background pre-warm/refresh of 1C services status for instant engineering console display
                try
                {
                    var serviceManager = scope.ServiceProvider.GetService<IOneSServiceManager>();
                    if (serviceManager != null)
                    {
                        _logger.LogInformation("Refreshing 1C services status in background...");
                        await serviceManager.GetAllServicesStatusAsync(forceRefresh: true, cancellationToken: stoppingToken);
                        _logger.LogInformation("1C services status refreshed and cached in background");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background refresh of 1C services status failed: {Msg}", ex.Message);
                }

                // Check if Confluence sync is due
                if (_config.EnableAutoSyncConfluence && CheckIfShouldSyncConfluence())
                {
                    await RunConfluenceSyncAsync(scope.ServiceProvider, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DiscoveryBackgroundWorker: {Message}", ex.Message);
            }

            int intervalMin = _config.DiscoveryIntervalMinutes > 0 ? _config.DiscoveryIntervalMinutes : 30;
            await Task.Delay(TimeSpan.FromMinutes(intervalMin), stoppingToken);
        }
    }

    private bool CheckIfShouldSyncConfluence()
    {
        var now = DateTime.Now;

        if (!string.IsNullOrWhiteSpace(_config.ConfluenceSyncTime) &&
            TimeSpan.TryParse(_config.ConfluenceSyncTime, out var targetTime))
        {
            if (now.Date > _lastConfluenceSyncDate && now.TimeOfDay >= targetTime)
            {
                return true;
            }
            return false;
        }

        int intervalHours = _config.ConfluenceSyncIntervalHours > 0 ? _config.ConfluenceSyncIntervalHours : 24;
        return (DateTime.UtcNow - _lastConfluenceSyncTime).TotalHours >= intervalHours;
    }

    private async Task RunConfluenceSyncAsync(IServiceProvider sp, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting scheduled Confluence synchronization cycle...");

        try
        {
            var publisher = sp.GetRequiredService<IConfluencePublisher>();
            var notifier = sp.GetRequiredService<INotificationService>();

            var devBases = _cache.GetDev();
            var prodBases = _cache.GetProd();

            bool success = await publisher.PublishAllAsync(devBases, prodBases, cancellationToken);
            if (success)
            {
                _lastConfluenceSyncDate = DateTime.Now.Date;
                _lastConfluenceSyncTime = DateTime.UtcNow;
                _cache.MarkConfluenceSyncCompleted();
                _logger.LogInformation("Scheduled Confluence sync successfully published for DEV, PROD and SA_INFO.");
            }
            else
            {
                _logger.LogWarning("Scheduled Confluence publishing encountered errors. Sending alert email...");
                await notifier.SendErrorAlertAsync(
                    "OneSGetDatabases.Web: Ошибка автоматической публикации баз в Confluence",
                    ["Не удалось обновить страницы баз 1С в Confluence. Подробности смотрите в журнале службы."],
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete scheduled Confluence sync cycle: {Message}", ex.Message);
        }
    }
}
