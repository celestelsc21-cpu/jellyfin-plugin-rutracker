using System;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.RuTracker.Configuration;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.RuTracker.Access;

/// <summary>
/// Default <see cref="IAccessService"/> backed by <see cref="IUserManager"/>.
/// Configuration is read on every call so role changes apply immediately.
/// </summary>
internal sealed class AccessService : IAccessService
{
    private readonly IUserManager _userManager;
    private readonly IPluginConfigurationAccessor _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="AccessService"/> class.
    /// </summary>
    /// <param name="userManager">User manager.</param>
    /// <param name="config">Configuration accessor.</param>
    public AccessService(IUserManager userManager, IPluginConfigurationAccessor config)
    {
        _userManager = userManager;
        _config = config;
    }

    /// <inheritdoc />
    public AccessInfo GetAccess(Guid userId)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return AccessInfo.None;
        }

        return AccessPolicy.Evaluate(
            userId,
            user.HasPermission(PermissionKind.IsAdministrator),
            user.HasPermission(PermissionKind.IsDisabled),
            _config.Current);
    }
}
