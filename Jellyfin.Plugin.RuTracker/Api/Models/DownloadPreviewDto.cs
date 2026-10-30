namespace Jellyfin.Plugin.RuTracker.Api.Models;

/// <summary>
/// Where a download will be placed.
/// </summary>
/// <param name="Folder">Destination name and folder inside it.</param>
/// <param name="ExistingFolder">The movie or series folder already exists and is reused.</param>
public sealed record DownloadPreviewDto(string Folder, bool ExistingFolder);
