using System;
using Jellyfin.Plugin.RuTracker.Configuration;

namespace Jellyfin.Plugin.RuTracker.Api.Models;

/// <summary>
/// A search result row as returned to the UI.
/// </summary>
/// <param name="TopicId">RuTracker topic id.</param>
/// <param name="Title">Topic title.</param>
/// <param name="Kind">Media kind.</param>
/// <param name="ForumName">Forum display name.</param>
/// <param name="SizeBytes">Size in bytes.</param>
/// <param name="Seeders">Seeder count.</param>
/// <param name="Leechers">Leecher count.</param>
/// <param name="Downloads">Completed download count.</param>
/// <param name="Added">Registration date.</param>
/// <param name="TopicUrl">Link to the topic on RuTracker.</param>
/// <param name="Author">Uploader name.</param>
/// <param name="HasSubtitles">The title announces subtitles.</param>
public sealed record SearchItemDto(
    long TopicId,
    string Title,
    MediaKind Kind,
    string ForumName,
    long SizeBytes,
    int Seeders,
    int Leechers,
    int Downloads,
    DateTimeOffset? Added,
    Uri TopicUrl,
    string Author,
    bool HasSubtitles);
