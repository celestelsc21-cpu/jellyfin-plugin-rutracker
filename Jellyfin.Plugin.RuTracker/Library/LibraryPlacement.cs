using Jellyfin.Plugin.RuTracker.Metadata;

namespace Jellyfin.Plugin.RuTracker.Library;

/// <summary>
/// Where a release goes inside a destination folder.
/// </summary>
/// <param name="RelativeFolder">Folder relative to the destination ("Series (2008) [tmdbid-1]/Season 02").</param>
/// <param name="ExistingFolder">The movie or series folder already existed.</param>
/// <param name="Season">Season subfolder number, if any.</param>
/// <param name="Match">Chosen metadata match, if any.</param>
internal sealed record LibraryPlacement(string RelativeFolder, bool ExistingFolder, int? Season, MetadataMatch? Match);
