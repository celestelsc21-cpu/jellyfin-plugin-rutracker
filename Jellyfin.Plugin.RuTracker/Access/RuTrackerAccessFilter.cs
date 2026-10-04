using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RuTracker.Access;

/// <summary>
/// Authorization filter enforcing a plugin role. Runs after Jellyfin's
/// <c>[Authorize]</c>, so the caller is already authenticated.
/// </summary>
internal sealed class RuTrackerAccessFilter : IAuthorizationFilter
{
    private readonly AccessRole _role;
    private readonly IAccessService _accessService;
    private readonly ILogger<RuTrackerAccessFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RuTrackerAccessFilter"/> class.
    /// </summary>
    /// <param name="role">Required role.</param>
    /// <param name="accessService">Access service.</param>
    /// <param name="logger">Logger.</param>
    public RuTrackerAccessFilter(AccessRole role, IAccessService accessService, ILogger<RuTrackerAccessFilter> logger)
    {
        _role = role;
        _accessService = accessService;
        _logger = logger;
    }

    /// <inheritdoc />
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var principal = context.HttpContext.User;

        if (CallerIdentity.IsApiKey(principal))
        {
            return; // API keys are unrestricted in Jellyfin.
        }

        var userId = CallerIdentity.GetUserId(principal);
        if (userId == Guid.Empty)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (!_accessService.GetAccess(userId).Has(_role))
        {
            _logger.LogInformation("RuTracker access denied: user {UserId} lacks role {Role}", userId, _role);
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        }
    }
}
