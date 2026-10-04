using System.ComponentModel.DataAnnotations;

namespace Jellyfin.Plugin.RuTracker.Api.Models;

/// <summary>
/// Request to change the episode to watch first.
/// </summary>
public sealed class SetStartRequest
{
    /// <summary>
    /// Gets or sets the file index inside the torrent.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int FileIndex { get; set; }
}
