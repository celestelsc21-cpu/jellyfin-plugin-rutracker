using System;
using System.ComponentModel.DataAnnotations;
using Jellyfin.Plugin.RuTracker.Configuration;

namespace Jellyfin.Plugin.RuTracker.Api.Models;

/// <summary>
/// Request to start a download.
/// </summary>
public sealed class CreateDownloadRequest
{
    /// <summary>
    /// Gets or sets the RuTracker topic id.
    /// </summary>
    [Range(1, long.MaxValue)]
    public long TopicId { get; set; }

    /// <summary>
    /// Gets or sets the media kind.
    /// </summary>
    public MediaKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the destination folder; <c>null</c> selects the default folder for the kind.
    /// </summary>
    public Guid? TargetId { get; set; }

    /// <summary>
    /// Gets or sets the file to watch first; <c>null</c> means the first episode.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int? StartFileIndex { get; set; }

    /// <summary>
    /// Gets or sets the 1-based episode number to start with, for torrents whose file list
    /// is not known yet (magnet links). Ignored when <see cref="StartFileIndex"/> is set.
    /// </summary>
    [Range(1, 10000)]
    public int? StartEpisodeNumber { get; set; }
}
