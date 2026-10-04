using System;
using System.Net.Mime;
using Jellyfin.Plugin.RuTracker.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.RuTracker.Api;

/// <summary>
/// Reports the caller's plugin permissions (used by the UI to show/hide actions).
/// </summary>
[ApiController]
[Authorize]
[Route("RuTracker/Access")]
[Produces(MediaTypeNames.Application.Json)]
public class AccessController : ControllerBase
{
    private readonly IAccessService _accessService;

    /// <summary>
    /// Initializes a new instance of the <see cref="AccessController"/> class.
    /// </summary>
    /// <param name="accessService">Access service.</param>
    public AccessController(IAccessService accessService)
    {
        _accessService = accessService;
    }

    /// <summary>
    /// Gets the caller's plugin permissions.
    /// </summary>
    /// <response code="200">Permissions returned.</response>
    /// <returns>Effective permissions.</returns>
    [HttpGet("Me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<AccessInfo> GetMine()
    {
        if (CallerIdentity.IsApiKey(User))
        {
            return new AccessInfo(true, true, true);
        }

        var userId = CallerIdentity.GetUserId(User);
        return userId == Guid.Empty ? AccessInfo.None : _accessService.GetAccess(userId);
    }
}
