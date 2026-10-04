using System;

namespace Jellyfin.Plugin.RuTracker.Access;

/// <summary>
/// Resolves plugin permissions for Jellyfin users.
/// </summary>
public interface IAccessService
{
    /// <summary>
    /// Gets effective permissions of a user.
    /// </summary>
    /// <param name="userId">User identifier.</param>
    /// <returns>Effective permissions; <see cref="AccessInfo.None"/> for unknown users.</returns>
    AccessInfo GetAccess(Guid userId);
}
