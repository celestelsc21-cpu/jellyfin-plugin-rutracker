using System.Collections.Generic;

namespace Jellyfin.Plugin.RuTracker.Api.Models;

/// <summary>
/// Where a download will be placed.
/// </summary>
/// <param name="Folder">Destination name and folder inside it.</param>
/// <param name="ExistingFolder">The movie or series folder already exists and is reused.</param>
public sealed record DownloadPreviewDto(string Folder, bool ExistingFolder, IReadOnlyList<EpisodeRenamePreviewDto> EpisodeRenames);

/// <summary>
/// A proposed episode file rename.
/// </summary>
/// <param name="OldPath">Original file path.</param>
/// <param name="NewPath">New file path.</param>
public sealed record EpisodeRenamePreviewDto(string OldPath, string NewPath);
