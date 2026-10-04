using System;
using System.Linq;
using Jellyfin.Plugin.RuTracker.Configuration;

namespace Jellyfin.Plugin.RuTracker.Access;

/// <summary>
/// Pure role evaluation, independent of the server.
/// </summary>
internal static class AccessPolicy
{
    /// <summary>
    /// Computes effective permissions.
    /// </summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="isAdministrator">User is an administrator.</param>
    /// <param name="isDisabled">User account is disabled.</param>
    /// <param name="config">Plugin configuration.</param>
    /// <returns>Effective permissions.</returns>
    public static AccessInfo Evaluate(Guid userId, bool isAdministrator, bool isDisabled, PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (isDisabled || userId == Guid.Empty)
        {
            return AccessInfo.None;
        }

        if (isAdministrator)
        {
            return new AccessInfo(true, true, true);
        }

        var canDownload = (config.DownloadUserIds ?? []).Contains(userId);
        var canSearch = canDownload || (config.SearchUserIds ?? []).Contains(userId);
        return new AccessInfo(false, canSearch, canDownload);
    }
}
