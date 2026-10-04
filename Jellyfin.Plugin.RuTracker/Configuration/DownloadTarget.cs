using System;

namespace Jellyfin.Plugin.RuTracker.Configuration;

/// <summary>
/// A folder downloads can be placed into. It is configured once, as the
/// Jellyfin container sees it; the qBittorrent-side path is derived through
/// <see cref="PluginConfiguration.PathMappings"/>.
/// </summary>
public class DownloadTarget
{
    /// <summary>
    /// Gets or sets the stable identifier of the target.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the media kind this target is used for.
    /// </summary>
    public MediaKind Kind { get; set; } = MediaKind.Movie;

    /// <summary>
    /// Gets or sets the display name shown when choosing a destination.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the folder inside the Jellyfin container, e.g. <c>/data/video/Фильмы</c>.
    /// </summary>
    public string JellyfinPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this is the default target for its kind.
    /// </summary>
    public bool IsDefault { get; set; }
}
