namespace Jellyfin.Plugin.RuTracker.Api.Models;

/// <summary>
/// A proposed episode file rename.
/// </summary>
/// <param name="OldPath">Original file path.</param>
/// <param name="NewPath">New file path.</param>
public sealed record EpisodeRenamePreviewDto(string OldPath, string NewPath);
