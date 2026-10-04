namespace Jellyfin.Plugin.RuTracker.Configuration;

/// <summary>
/// Maps a folder prefix as the Jellyfin container sees it to the same folder
/// as qBittorrent (Windows) sees it, e.g. <c>/data/video</c> ↔ <c>C:\video</c>.
/// </summary>
public class PathMapping
{
    /// <summary>
    /// Gets or sets the prefix inside the Jellyfin container, e.g. <c>/data/video</c>.
    /// </summary>
    public string JellyfinPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the prefix on the qBittorrent machine, e.g. <c>C:\video</c>.
    /// </summary>
    public string QBittorrentPrefix { get; set; } = string.Empty;
}
