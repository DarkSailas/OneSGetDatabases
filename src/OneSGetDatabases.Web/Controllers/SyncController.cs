using Microsoft.AspNetCore.Mvc;
using OneSGetDatabases.Core.Interfaces;

namespace OneSGetDatabases.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private readonly IInfobaseDiscoveryService _discoveryService;
    private readonly IConfluencePublisher _confluencePublisher;
    private readonly IDatabaseCacheService _cache;
    private readonly ILogger<SyncController> _logger;

    private static bool _isScanning = false;
    private static bool _isSyncingConfluence = false;
    private static readonly object _lock = new();

    public SyncController(
        IInfobaseDiscoveryService discoveryService,
        IConfluencePublisher confluencePublisher,
        IDatabaseCacheService cache,
        ILogger<SyncController> logger)
    {
        _discoveryService = discoveryService;
        _confluencePublisher = confluencePublisher;
        _cache = cache;
        _logger = logger;
    }

    [HttpGet("status")]
    public ActionResult<object> GetStatus()
    {
        return Ok(new
        {
            IsScanning = _isScanning,
            IsSyncingConfluence = _isSyncingConfluence,
            LastScanTime = _cache.LastScanTime,
            LastConfluenceSyncTime = _cache.LastConfluenceSyncTime,
            TotalDatabases = _cache.GetAll().Count
        });
    }

    [HttpPost("scan")]
    public async Task<ActionResult<object>> TriggerScan(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_isScanning)
            {
                return Conflict(new { Message = "Процесс сканирования уже выполняется" });
            }
            _isScanning = true;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("Manual discovery triggered via API");
                var bases = await _discoveryService.DiscoverAllAsync(CancellationToken.None);
                _cache.Update(bases);
                _logger.LogInformation("Manual discovery completed successfully. Total bases: {Count}", bases.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in manual discovery: {Msg}", ex.Message);
            }
            finally
            {
                lock (_lock) { _isScanning = false; }
            }
        });

        return Accepted(new { Message = "Сканирование кластеров успешно запущено в фоновом режиме" });
    }

    [HttpPost("confluence")]
    public async Task<ActionResult<object>> TriggerConfluenceSync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_isSyncingConfluence)
            {
                return Conflict(new { Message = "Синхронизация с Confluence уже выполняется" });
            }
            _isSyncingConfluence = true;
        }

        try
        {
            var dev = _cache.GetDev();
            var prod = _cache.GetProd();

            if (dev.Count == 0 && prod.Count == 0)
            {
                var bases = await _discoveryService.DiscoverAllAsync(cancellationToken);
                _cache.Update(bases);
                dev = _cache.GetDev();
                prod = _cache.GetProd();
            }

            bool success = await _confluencePublisher.PublishAllAsync(dev, prod, cancellationToken);
            if (success)
            {
                _cache.MarkConfluenceSyncCompleted();
                return Ok(new { Message = "Данные успешно опубликованы в Confluence", Timestamp = DateTime.UtcNow });
            }
            else
            {
                return StatusCode(500, new { Message = "Ошибка при обновлении страниц в Confluence" });
            }
        }
        finally
        {
            lock (_lock) { _isSyncingConfluence = false; }
        }
    }
}
