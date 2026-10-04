namespace Jellyfin.Plugin.RuTracker.Api.Models;

/// <summary>
/// A download folder and its translated qBittorrent path.
/// </summary>
/// <param name="Name">Display name.</param>
/// <param name="Kind">Media kind display name.</param>
/// <param name="JellyfinPath">Path inside the Jellyfin container.</param>
/// <param name="QBittorrentPath">Path on the qBittorrent machine, or <c>null</c> when unmapped.</param>
public sealed record MappedFolder(string Name, string Kind, string JellyfinPath, string? QBittorrentPath);
