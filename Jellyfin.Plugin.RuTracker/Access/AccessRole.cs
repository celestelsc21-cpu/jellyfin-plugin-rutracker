namespace Jellyfin.Plugin.RuTracker.Access;

/// <summary>
/// Plugin roles. Administrators implicitly hold every role.
/// </summary>
public enum AccessRole
{
    /// <summary>
    /// May search RuTracker and view torrent lists.
    /// </summary>
    Search = 0,

    /// <summary>
    /// May start downloads and manage subscriptions. Implies <see cref="Search"/>.
    /// </summary>
    Download = 1
}
