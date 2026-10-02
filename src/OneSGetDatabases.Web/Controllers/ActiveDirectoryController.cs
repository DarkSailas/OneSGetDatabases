using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;
using OneSGetDatabases.Core.Services;

namespace OneSGetDatabases.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ActiveDirectoryController : ControllerBase
{
    private readonly IActiveDirectoryService _adService;
    private readonly IAuditLogService _auditLog;
    private readonly ActiveDirectoryConfig _adConfig;

    public ActiveDirectoryController(
        IActiveDirectoryService adService,
        IAuditLogService auditLog,
        IOptions<ActiveDirectoryConfig> adConfig)
    {
        _adService = adService;
        _auditLog = auditLog;
        _adConfig = adConfig.Value;
    }

    [HttpGet("group/{groupName}/members")]
    public async Task<ActionResult<AdGroupDetails>> GetGroupMembers(string groupName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return BadRequest(new { Message = "Имя группы не указано" });
        }

        var result = await _adService.GetGroupMembersAsync(groupName, cancellationToken);
        if (!string.IsNullOrEmpty(result.Error) && result.Members.Count == 0)
        {
            return NotFound(result);
        }

        return Ok(result);
    }

    [HttpGet("group/{groupName}/manageable")]
    public ActionResult GetManageable(string groupName)
    {
        return Ok(new { manageable = _adService.IsManagedGroup(groupName) });
    }

    [HttpGet("users/search")]
    public async Task<ActionResult<IReadOnlyList<AdGroupMember>>> SearchUsers([FromQuery] string q, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return Ok(Array.Empty<AdGroupMember>());
        }

        try
        {
            var users = await _adService.SearchUsersAsync(q, 20, cancellationToken);
            return Ok(users);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return StatusCode(500, new { error = $"Ошибка поиска в Active Directory: {ex.Message}" });
        }
    }

    [HttpPost("group/{groupName}/members")]
    public async Task<ActionResult<AdMembershipResult>> AddMember(
        string groupName,
        [FromBody] AdMemberChangeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(groupName) || string.IsNullOrWhiteSpace(request?.SamAccountName))
        {
            return BadRequest(new AdMembershipResult(false, "Не указаны группа или логин пользователя"));
        }

        var sw = Stopwatch.StartNew();
        var result = await _adService.AddMemberAsync(groupName, request.SamAccountName, cancellationToken);
        await WriteAuditAsync("AD_ADD_MEMBER", groupName, request.SamAccountName, result, sw.ElapsedMilliseconds);
        return ToActionResult(result);
    }

    [HttpDelete("group/{groupName}/members/{samAccountName}")]
    public async Task<ActionResult<AdMembershipResult>> RemoveMember(
        string groupName,
        string samAccountName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(groupName) || string.IsNullOrWhiteSpace(samAccountName))
        {
            return BadRequest(new AdMembershipResult(false, "Не указаны группа или логин пользователя"));
        }

        var sw = Stopwatch.StartNew();
        var result = await _adService.RemoveMemberAsync(groupName, samAccountName, cancellationToken);
        await WriteAuditAsync("AD_REMOVE_MEMBER", groupName, samAccountName, result, sw.ElapsedMilliseconds);
        return ToActionResult(result);
    }

    private ActionResult<AdMembershipResult> ToActionResult(AdMembershipResult result)
    {
        if (result.Success) return Ok(result);
        if (result.Forbidden) return StatusCode(403, result);
        if (result.NotFound) return NotFound(result);
        return StatusCode(500, result);
    }

    private async Task WriteAuditAsync(string action, string groupName, string sam, AdMembershipResult result, long durationMs)
    {
        string clientIp = GetClientIp();
        // The audit write must not be cancelled by a client disconnect after AD was already changed
        await _auditLog.LogActionAsync(new AuditLogEntry
        {
            ClientIp = clientIp,
            ClientHostName = AuditLogService.ResolveHostName(clientIp),
            Host = _adConfig.Domain,
            ClusterPort = 0,
            ServiceName = groupName,
            DisplayName = $"Группа AD {groupName}",
            Action = action,
            Status = result.Success ? "SUCCESS" : "FAILED",
            ErrorMessage = $"{sam}: {result.Message}",
            DurationMs = durationMs
        }, CancellationToken.None);
    }

    private string GetClientIp() => ClientAddress.Get(HttpContext);
}
