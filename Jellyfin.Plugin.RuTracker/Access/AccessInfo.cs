namespace Jellyfin.Plugin.RuTracker.Access;

/// <summary>
/// Effective plugin permissions of a caller.
/// </summary>
/// <param name="IsAdministrator">Caller is a server administrator (or uses an API key).</param>
/// <param name="CanSearch">Caller may search.</param>
/// <param name="CanDownload">Caller may start downloads.</param>
public sealed record AccessInfo(bool IsAdministrator, bool CanSearch, bool CanDownload)
{
    /// <summary>
    /// Gets an instance with no permissions.
    /// </summary>
    public static AccessInfo None { get; } = new(false, false, false);

    /// <summary>
    /// Checks a role.
    /// </summary>
    /// <param name="role">Role to check.</param>
    /// <returns><c>true</c> if granted.</returns>
    public bool Has(AccessRole role) => role switch
    {
        AccessRole.Search => CanSearch,
        AccessRole.Download => CanDownload,
        _ => false
    };
}
