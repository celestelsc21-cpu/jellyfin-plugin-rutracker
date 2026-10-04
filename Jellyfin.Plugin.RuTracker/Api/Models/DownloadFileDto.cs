namespace Jellyfin.Plugin.RuTracker.Api.Models;

/// <summary>
/// A video file of an active download.
/// </summary>
/// <param name="Index">File index inside the torrent.</param>
/// <param name="Label">Display label.</param>
/// <param name="Progress">Progress, 0..1.</param>
/// <param name="IsCurrent">This file is being prioritised now.</param>
public sealed record DownloadFileDto(int Index, string Label, double Progress, bool IsCurrent);
