using System.Collections.Generic;

namespace Jellyfin.Plugin.RuTracker.Metadata;

/// <summary>
/// A match kept on the server, so the client only sends its key back.
/// </summary>
/// <param name="Name">Title.</param>
/// <param name="Year">Year.</param>
/// <param name="Overview">Description.</param>
/// <param name="ImageUrl">Poster URL from the provider.</param>
/// <param name="ProviderIds">Provider ids.</param>
/// <param name="Tag">Folder tag.</param>
internal sealed record MetadataMatch(string Name, int? Year, string? Overview, string? ImageUrl, IReadOnlyDictionary<string, string> ProviderIds, string Tag);
