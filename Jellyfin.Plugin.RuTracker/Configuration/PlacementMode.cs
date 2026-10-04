namespace Jellyfin.Plugin.RuTracker.Configuration;

/// <summary>
/// How downloaded files reach a Jellyfin library.
/// </summary>
public enum PlacementMode
{
    /// <summary>
    /// qBittorrent writes directly into the library folder; folders with
    /// incomplete content are hidden from the scanner until a file completes.
    /// No extra disk space or network copy is needed.
    /// </summary>
    DownloadIntoLibrary = 0,

    /// <summary>
    /// qBittorrent writes to a staging folder; each completed file is copied
    /// into the library. Requires double disk space while seeding.
    /// </summary>
    CopyAfterComplete = 1
}
