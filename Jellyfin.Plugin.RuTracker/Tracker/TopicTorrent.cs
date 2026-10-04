using System;
using Jellyfin.Plugin.RuTracker.Torrents;

namespace Jellyfin.Plugin.RuTracker.Tracker;

/// <summary>
/// What is needed to hand a RuTracker topic to qBittorrent.
/// </summary>
/// <param name="TopicId">Topic id.</param>
/// <param name="Title">Topic title.</param>
/// <param name="TorrentFile">The .torrent file bytes; empty when RuTracker did not provide it.</param>
/// <param name="Meta">Parsed metadata of <paramref name="TorrentFile"/>.</param>
/// <param name="Magnet">Magnet link from the topic page (fallback).</param>
public sealed record TopicTorrent(long TopicId, string Title, ReadOnlyMemory<byte> TorrentFile, TorrentMeta? Meta, string? Magnet)
{
    /// <summary>
    /// Gets the v1 info hash (lowercase hex), or <c>null</c> when unknown.
    /// </summary>
    public string? InfoHash => Meta?.InfoHash ?? MagnetLink.GetInfoHash(Magnet);
}
