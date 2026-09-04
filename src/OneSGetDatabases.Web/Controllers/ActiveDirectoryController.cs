using Microsoft.AspNetCore.Mvc;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ActiveDirectoryController : ControllerBase
{
    private readonly IActiveDirectoryService _adService;

    public ActiveDirectoryController(IActiveDirectoryService adService)
    {
        _adService = adService;
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
}
