using Microsoft.AspNetCore.Mvc;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServicesController : ControllerBase
{
    private readonly IOneSServiceManager _serviceManager;
    private readonly IAuditLogService _auditLog;

    public ServicesController(
        IOneSServiceManager serviceManager,
        IAuditLogService auditLog)
    {
        _serviceManager = serviceManager;
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OneSServiceInfo>>> GetServices(
        [FromQuery] bool force = false,
        CancellationToken cancellationToken = default)
    {
        var services = await _serviceManager.GetAllServicesStatusAsync(forceRefresh: force, cancellationToken: cancellationToken);
        return Ok(services);
    }

    [HttpPost("action")]
    public async Task<ActionResult<ServiceActionResult>> ExecuteAction(
        [FromBody] ServiceActionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Host) || string.IsNullOrWhiteSpace(request.ServiceName) || string.IsNullOrWhiteSpace(request.Action))
        {
            return BadRequest(new ServiceActionResult
            {
                Success = false,
                Message = "Не указан сервер, имя службы или действие"
            });
        }

        string clientIp = GetClientIp();
        var result = await _serviceManager.ExecuteServiceActionAsync(request, clientIp, cancellationToken);
        return Ok(result);
    }

    [HttpGet("audit")]
    public async Task<ActionResult<IReadOnlyList<AuditLogEntry>>> GetAuditLog(
        [FromQuery] int limit = 200,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var entries = await _auditLog.GetEntriesAsync(limit, search, cancellationToken);
        return Ok(entries);
    }

    [HttpPost("audit/event")]
    public async Task<IActionResult> LogConsoleEvent(
        [FromBody] ConsoleAuditEventRequest request,
        CancellationToken cancellationToken)
    {
        string clientIp = GetClientIp();
        var entry = new AuditLogEntry
        {
            ClientIp = clientIp,
            ClientHostName = OneSGetDatabases.Core.Services.AuditLogService.ResolveHostName(clientIp),
            Host = string.Empty,
            ClusterPort = 0,
            ServiceName = request.ConsoleName switch
            {
                "SERVICES" => "1C_Services_Console",
                "RESTORE" => "Sql_Restore_Console",
                _ => "Audit_Console"
            },
            DisplayName = request.ConsoleName switch
            {
                "SERVICES" => "Консоль: Управление службами 1С",
                "RESTORE" => "Консоль: Восстановление баз",
                _ => "Консоль: Журнал аудита"
            },
            Action = request.Action,
            Status = "SUCCESS",
            DurationMs = 0
        };

        await _auditLog.LogActionAsync(entry, cancellationToken);
        return Ok(new { success = true });
    }

    private string GetClientIp() => ClientAddress.Get(HttpContext);
}
