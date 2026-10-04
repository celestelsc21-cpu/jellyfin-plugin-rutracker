using System;
using System.Collections.Generic;
using Jellyfin.Plugin.RuTracker.Configuration;

namespace Jellyfin.Plugin.RuTracker.Downloads;

/// <summary>
/// A download started from the plugin, persisted between server restarts.
/// </summary>
internal sealed class DownloadRecord
{
    /// <summary>
    /// Gets or sets the identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the RuTracker topic id.
    /// </summary>
    public long TopicId { get; set; }

    /// <summary>
    /// Gets or sets the topic title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the media kind.
    /// </summary>
    public MediaKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the download target id.
    /// </summary>
    public Guid TargetId { get; set; }

    /// <summary>
    /// Gets or sets the folder inside the Jellyfin container.
    /// </summary>
    public string JellyfinFolder { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the folder as qBittorrent sees it.
    /// </summary>
    public string QBittorrentFolder { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the info hash (lowercase hex).
    /// </summary>
    public string InfoHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user who started the download.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the creation time.
    /// </summary>
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the file the user wants to watch first; <c>null</c> means the first episode.
    /// </summary>
    public int? StartFileIndex { get; set; }

    /// <summary>
    /// Gets or sets the watching order (video file indexes); empty until the file list is known.
    /// </summary>
    public List<int> Order { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the plugin has started the torrent after setting priorities.
    /// </summary>
    public bool Started { get; set; }

    /// <summary>
    /// Gets or sets the state.
    /// </summary>
    public DownloadState State { get; set; } = DownloadState.Queued;
}
