namespace Jellyfin.Plugin.RuTracker.Streaming;

/// <summary>
/// A video file of an active download, shown as a playable item in the channel.
/// </summary>
/// <param name="Index">File index inside the torrent.</param>
/// <param name="Number">1-based position in natural order.</param>
/// <param name="Name">Path inside the torrent.</param>
/// <param name="Label">Display label.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Progress">Progress, 0..1.</param>
public sealed record ChannelFile(int Index, int Number, string Name, string Label, long Size, double Progress);
