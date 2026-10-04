using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.RuTracker.QBittorrent;

/// <summary>
/// Torrent state from <c>/api/v2/torrents/info</c> (subset).
/// </summary>
public sealed class QbTorrent
{
    /// <summary>
    /// Gets or sets the info hash (lowercase hex).
    /// </summary>
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the torrent name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the qBittorrent state, e.g. <c>downloading</c>, <c>stoppedDL</c>, <c>uploading</c>.
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the progress, 0..1.
    /// </summary>
    [JsonPropertyName("progress")]
    public double Progress { get; set; }

    /// <summary>
    /// Gets or sets the download speed, bytes per second.
    /// </summary>
    [JsonPropertyName("dlspeed")]
    public long DownloadSpeed { get; set; }

    /// <summary>
    /// Gets or sets the estimated time left, seconds (8640000 means unknown).
    /// </summary>
    [JsonPropertyName("eta")]
    public long Eta { get; set; }

    /// <summary>
    /// Gets or sets the total size selected for download, bytes.
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }
}
