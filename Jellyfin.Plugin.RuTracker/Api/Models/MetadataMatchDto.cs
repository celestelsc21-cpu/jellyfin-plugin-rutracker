namespace Jellyfin.Plugin.RuTracker.Api.Models;

/// <summary>
/// A movie or series found in the metadata providers connected to Jellyfin (TMDb, TVDB, ...).
/// </summary>
/// <param name="Key">Opaque key to send back when starting the download.</param>
/// <param name="Name">Title.</param>
/// <param name="Year">Year (first air year for a series).</param>
/// <param name="Overview">Short description.</param>
/// <param name="ImageUrl">Poster URL, if any.</param>
/// <param name="Provider">Provider that found it.</param>
/// <param name="Tag">Folder tag, e.g. <c>tmdbid-1396</c>.</param>
public sealed record MetadataMatchDto(string Key, string Name, int? Year, string? Overview, string? ImageUrl, string Provider, string Tag);
