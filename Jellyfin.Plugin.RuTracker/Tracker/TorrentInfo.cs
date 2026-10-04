using System;
using Jellyfin.Plugin.RuTracker.Configuration;

namespace Jellyfin.Plugin.RuTracker.Tracker;

/// <summary>
/// A torrent (topic) found on RuTracker.
/// </summary>
/// <param name="TopicId">Topic id (<c>viewtopic.php?t=</c>).</param>
/// <param name="Title">Topic title as published.</param>
/// <param name="ForumId">Forum (category) id.</param>
/// <param name="ForumName">Forum display name.</param>
/// <param name="Kind">Media kind, or <c>null</c> when the forum is not video content.</param>
/// <param name="SizeBytes">Total size in bytes.</param>
/// <param name="Seeders">Seeder count.</param>
/// <param name="Leechers">Leecher count.</param>
/// <param name="Downloads">Completed download count.</param>
/// <param name="Added">Registration date of the torrent, when known.</param>
/// <param name="Author">Uploader name.</param>
public sealed record TorrentInfo(
    long TopicId,
    string Title,
    int ForumId,
    string ForumName,
    MediaKind? Kind,
    long SizeBytes,
    int Seeders,
    int Leechers,
    int Downloads,
    DateTimeOffset? Added,
    string Author);
