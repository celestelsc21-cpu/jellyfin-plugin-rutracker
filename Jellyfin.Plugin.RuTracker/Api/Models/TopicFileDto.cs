namespace Jellyfin.Plugin.RuTracker.Api.Models;

/// <summary>
/// A file of a torrent, for choosing where to start watching.
/// </summary>
/// <param name="Index">File index inside the torrent.</param>
/// <param name="Path">Path inside the torrent.</param>
/// <param name="Label">Short display label (e.g. "S01E02 — name").</param>
/// <param name="SizeBytes">Size in bytes.</param>
/// <param name="IsVideo">The file is a video.</param>
public sealed record TopicFileDto(int Index, string Path, string Label, long SizeBytes, bool IsVideo);
