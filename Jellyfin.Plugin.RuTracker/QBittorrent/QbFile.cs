using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.RuTracker.QBittorrent;

/// <summary>
/// File state from <c>/api/v2/torrents/files</c>.
/// </summary>
public sealed class QbFile
{
    /// <summary>
    /// Gets or sets the file index (matches the torrent file order).
    /// </summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets the path inside the torrent.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the size, bytes.
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the progress, 0..1.
    /// </summary>
    [JsonPropertyName("progress")]
    public double Progress { get; set; }

    /// <summary>
    /// Gets or sets the priority (0 skip, 1 normal, 6 high, 7 maximal).
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    /// <summary>
    /// Gets or sets the first and last piece the file touches.
    /// </summary>
    [JsonPropertyName("piece_range")]
    public IReadOnlyList<int> PieceRange { get; set; } = [];
}
