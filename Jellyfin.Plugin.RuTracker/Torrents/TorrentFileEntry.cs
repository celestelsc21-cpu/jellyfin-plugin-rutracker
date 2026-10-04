namespace Jellyfin.Plugin.RuTracker.Torrents;

/// <summary>
/// A file inside a torrent. <see cref="Index"/> matches qBittorrent's file index.
/// </summary>
/// <param name="Index">Zero-based file index in torrent order.</param>
/// <param name="Path">Relative path inside the torrent (forward slashes).</param>
/// <param name="Length">Size in bytes.</param>
public sealed record TorrentFileEntry(int Index, string Path, long Length);
