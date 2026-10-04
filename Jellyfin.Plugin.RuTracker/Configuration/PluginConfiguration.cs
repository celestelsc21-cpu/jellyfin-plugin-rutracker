using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.RuTracker.Configuration;

/// <summary>
/// Plugin configuration. Defaults are safe: nothing is reachable for
/// non-administrators until an administrator grants roles explicitly.
/// </summary>
/// <remarks>
/// Arrays are used on purpose: the configuration is (de)serialized both by
/// XmlSerializer (on disk) and System.Text.Json (dashboard API), and plain
/// settable arrays round-trip reliably through both.
/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Current configuration schema version. Bump together with a migration step.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Gets or sets the schema version of this configuration instance.
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// Gets or sets the RuTracker base URL. Only https RuTracker hosts are accepted.
    /// </summary>
    public string RuTrackerBaseUrl { get; set; } = "https://rutracker.org";

    /// <summary>
    /// Gets or sets the RuTracker login.
    /// </summary>
    public string RuTrackerUsername { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the RuTracker password.
    /// </summary>
    public string RuTrackerPassword { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a manually supplied <c>bb_session</c> cookie, used when
    /// login is blocked by a captcha.
    /// </summary>
    public string RuTrackerSessionCookie { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the qBittorrent Web UI URL, e.g. <c>http://192.168.1.20:8080</c>.
    /// </summary>
    public string QBittorrentUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the qBittorrent Web UI login.
    /// </summary>
    public string QBittorrentUsername { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the qBittorrent Web UI password.
    /// </summary>
    public string QBittorrentPassword { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the qBittorrent category used to tag torrents owned by this plugin.
    /// </summary>
    public string QBittorrentCategory { get; set; } = "jellyfin-rutracker";

    /// <summary>
    /// Gets or sets how completed files reach the library.
    /// </summary>
    public PlacementMode Placement { get; set; } = PlacementMode.DownloadIntoLibrary;

    /// <summary>
    /// Gets or sets the staging folder inside the Jellyfin container
    /// (used only with <see cref="PlacementMode.CopyAfterComplete"/>; must be covered by a mapping).
    /// </summary>
    public string StagingJellyfinPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets prefix mappings between the Jellyfin container and the qBittorrent machine.
    /// </summary>
    public PathMapping[] PathMappings { get; set; } = [];

    /// <summary>
    /// Gets or sets the configured download destinations.
    /// </summary>
    public DownloadTarget[] DownloadTargets { get; set; } = [];

    /// <summary>
    /// Gets or sets users allowed to search RuTracker.
    /// </summary>
    public Guid[] SearchUserIds { get; set; } = [];

    /// <summary>
    /// Gets or sets users allowed to start downloads (implies search).
    /// </summary>
    public Guid[] DownloadUserIds { get; set; } = [];

    /// <summary>
    /// Gets or sets the interval of the update-tracking task, in hours.
    /// </summary>
    public int UpdateCheckIntervalHours { get; set; } = 12;
}
#pragma warning restore CA1819
