using System;
using Jellyfin.Plugin.RuTracker.Configuration;
using Jellyfin.Plugin.RuTracker.Downloads;

namespace Jellyfin.Plugin.RuTracker.Api.Models;

/// <summary>
/// A download as shown in the UI.
/// </summary>
/// <param name="Id">Download id.</param>
/// <param name="TopicId">RuTracker topic id.</param>
/// <param name="Title">Topic title.</param>
/// <param name="Kind">Media kind.</param>
/// <param name="TargetName">Destination folder name.</param>
/// <param name="State">State.</param>
/// <param name="Progress">Overall progress, 0..1.</param>
/// <param name="SpeedBytes">Download speed, bytes per second.</param>
/// <param name="EtaSeconds">Estimated time left, seconds; <c>null</c> when unknown.</param>
/// <param name="CurrentLabel">Episode being prioritised now.</param>
/// <param name="CurrentProgress">Progress of that episode, 0..1.</param>
/// <param name="VideoCount">Number of video files.</param>
/// <param name="VideosDone">Number of finished video files.</param>
/// <param name="Created">When the download was started.</param>
/// <param name="CanDelete">The caller may cancel it.</param>
public sealed record DownloadDto(
    Guid Id,
    long TopicId,
    string Title,
    MediaKind Kind,
    string TargetName,
    DownloadState State,
    double Progress,
    long SpeedBytes,
    long? EtaSeconds,
    string? CurrentLabel,
    double? CurrentProgress,
    int VideoCount,
    int VideosDone,
    DateTimeOffset Created,
    bool CanDelete);
