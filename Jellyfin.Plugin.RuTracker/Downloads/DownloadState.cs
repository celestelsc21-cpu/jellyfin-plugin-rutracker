namespace Jellyfin.Plugin.RuTracker.Downloads;

/// <summary>
/// Lifecycle of a plugin-managed download.
/// </summary>
public enum DownloadState
{
    /// <summary>
    /// Added to qBittorrent; waiting for metadata or the first priority pass.
    /// </summary>
    Queued = 0,

    /// <summary>
    /// Downloading.
    /// </summary>
    Downloading = 1,

    /// <summary>
    /// Every file is downloaded.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Removed from qBittorrent outside the plugin.
    /// </summary>
    Removed = 3
}
