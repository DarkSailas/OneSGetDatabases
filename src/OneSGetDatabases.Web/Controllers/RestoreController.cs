using System.Net;
using Microsoft.AspNetCore.Mvc;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RestoreController : ControllerBase
{
    private readonly ISqlRestoreService _restoreService;
    private readonly ILogger<RestoreController> _logger;

    public RestoreController(ISqlRestoreService restoreService, ILogger<RestoreController> logger)
    {
        _restoreService = restoreService;
        _logger = logger;
    }

    private string GetClientIp() => ClientAddress.Get(HttpContext);

    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        return Ok(new
        {
            ExcludedDatabases = _restoreService.GetExcludedDatabases(),
            ExcludedCatalogs = _restoreService.GetExcludedCatalogs()
        });
    }

    [HttpGet("sources")]
    public async ValueTask<IActionResult> GetSources(CancellationToken ct)
    {
        try
        {
            var sources = await _restoreService.GetBackupSourcesAsync(ct);
            return Ok(sources);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка получения каталогов архива бэкапов");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("timeline")]
    public async ValueTask<IActionResult> GetTimeline(
        [FromQuery] string server,
        [FromQuery] string database,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
            return BadRequest(new { error = "Параметры server и database обязательны." });

        try
        {
            var points = await _restoreService.GetTimelinePointsAsync(server, database, ct);
            return Ok(points);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка построения точек восстановления для {Server}/{Db}", server, database);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("plan")]
    public async ValueTask<IActionResult> GetPlan(
        [FromQuery] string server,
        [FromQuery] string database,
        [FromQuery] string pointId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(pointId))
            return BadRequest(new { error = "Параметры server, database и pointId обязательны." });

        var plan = await _restoreService.GetRestorePlanPreviewAsync(server, database, pointId, ct);
        return Ok(plan);
    }

    [HttpGet("diagnose")]
    public async ValueTask<IActionResult> Diagnose(
        [FromQuery] string server,
        [FromQuery] string database,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
            return BadRequest(new { error = "Параметры server и database обязательны." });

        var result = await _restoreService.DiagnoseCatalogAsync(server, database, ct);
        return Ok(result);
    }

    [HttpPost("start")]
    public async ValueTask<IActionResult> StartRestore(
        [FromBody] RestoreStartRequest request,
        CancellationToken ct)
    {
        if (request == null)
            return BadRequest(new { error = "Тело запроса не может быть пустым." });

        string clientIp = GetClientIp();

        try
        {
            var state = await _restoreService.StartRestoreAsync(request, clientIp, ct);
            return Ok(state);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Отказ в запуске восстановления: {Message}", ex.Message);
            return StatusCode(403, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка старта восстановления базы {Db} на {Server}", request.TargetDatabase, request.TargetServer);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("status/{id}")]
    public IActionResult GetStatus([FromRoute] string id)
    {
        var state = _restoreService.GetOperationStatus(id);
        if (state == null)
            return NotFound(new { error = "Операция не найдена." });

        return Ok(state);
    }

    [HttpGet("active")]
    public IActionResult GetActive()
    {
        var active = _restoreService.GetActiveOperations();
        return Ok(active);
    }
}
