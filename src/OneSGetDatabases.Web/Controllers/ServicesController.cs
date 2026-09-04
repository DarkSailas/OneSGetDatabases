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
            ServiceName = request.ConsoleName == "SERVICES" ? "1C_Services_Console" : "Audit_Console",
            DisplayName = request.ConsoleName == "SERVICES" ? "Консоль: Управление службами 1С" : "Консоль: Журнал аудита",
            Action = request.Action,
            Status = "SUCCESS",
            DurationMs = 0
        };

        await _auditLog.LogActionAsync(entry, cancellationToken);
        return Ok(new { success = true });
    }

    private string GetClientIp()
    {
        if (Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) && !string.IsNullOrWhiteSpace(forwardedFor))
        {
            var ip = forwardedFor.ToString().Split(',')[0].Trim();
            return NormalizeIp(ip);
        }

        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        if (remoteIp != null)
        {
            if (remoteIp.IsIPv4MappedToIPv6)
            {
                return remoteIp.MapToIPv4().ToString();
            }
            return NormalizeIp(remoteIp.ToString());
        }

        return "127.0.0.1";
    }

    private static string NormalizeIp(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return "127.0.0.1";
        if (ip.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
            ip = ip[7..];
        if (ip == "::1")
            return "127.0.0.1";
        return ip;
    }
}
