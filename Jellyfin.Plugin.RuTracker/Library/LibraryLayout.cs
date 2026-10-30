using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.RuTracker.Tracker;

namespace Jellyfin.Plugin.RuTracker.Library;

/// <summary>
/// Decides where a release goes in the library: one folder per movie or series
/// (all seasons of a series share it) and a "Season NN" subfolder for a single-season release.
/// </summary>
internal static partial class LibraryLayout
{
    /// <summary>
    /// Folder tags understood by Jellyfin's resolvers, in order of preference.
    /// </summary>
    private static readonly (string Provider, string Tag)[] Tags =
    [
        ("Tmdb", "tmdbid"),
        ("Tvdb", "tvdbid"),
        ("Imdb", "imdbid")
    ];

    /// <summary>
    /// Builds the folder tag (e.g. <c>tmdbid-1396</c>) from provider ids.
    /// </summary>
    /// <param name="providerIds">Provider ids (keys like "Tmdb").</param>
    /// <returns>The tag, or <c>null</c> when no supported id is present.</returns>
    public static string? FolderTag(IReadOnlyDictionary<string, string>? providerIds)
    {
        if (providerIds is null)
        {
            return null;
        }

        foreach (var (provider, tag) in Tags)
        {
            var value = providerIds.FirstOrDefault(p => string.Equals(p.Key, provider, StringComparison.OrdinalIgnoreCase)).Value;
            if (!string.IsNullOrWhiteSpace(value) && SafeId().IsMatch(value))
            {
                return tag + "-" + value;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds a folder name such as <c>Breaking Bad (2008) [tmdbid-1396]</c>.
    /// </summary>
    /// <param name="name">Title.</param>
    /// <param name="year">Year (first air year for a series).</param>
    /// <param name="tag">Folder tag, or <c>null</c>.</param>
    /// <returns>Folder name.</returns>
    public static string FolderName(string name, int? year, string? tag)
    {
        var folder = ReleaseTitle.Sanitize(name ?? string.Empty);
        if (folder.Length > 100)
        {
            folder = folder[..100].TrimEnd(' ', '.');
        }

        if (year is > 1800 and < 2200)
        {
            folder += " (" + year.Value.ToString(CultureInfo.InvariantCulture) + ")";
        }

        if (!string.IsNullOrEmpty(tag))
        {
            folder += " [" + tag + "]";
        }

        return folder;
    }

    /// <summary>
    /// Gets the season of a single-season release from its title ("Сезон: 2", "Season 2", "S02").
    /// </summary>
    /// <param name="title">Release title.</param>
    /// <returns>Season number, or <c>null</c> for several seasons or when not stated.</returns>
    public static int? SeasonNumber(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var match = SeasonPattern().Match(title);
        if (!match.Success)
        {
            return null;
        }

        // "Сезон: 1-3", "Сезоны: 1, 2" or "Сезон: 2 из 5"? Only the first form means several seasons.
        if (match.Groups["more"].Success)
        {
            return null;
        }

        return int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 0 and <= 100
            ? n
            : null;
    }

    /// <summary>
    /// Gets the name of a season folder.
    /// </summary>
    /// <param name="season">Season number.</param>
    /// <returns>For example "Season 02" (understood by Jellyfin in any language).</returns>
    public static string SeasonFolder(int season)
        => "Season " + season.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Finds a folder that already holds the same movie or series: first by provider tag,
    /// then by name ignoring the year and tags.
    /// </summary>
    /// <param name="existing">Names of the folders in the destination.</param>
    /// <param name="tag">Folder tag of the match, or <c>null</c>.</param>
    /// <param name="names">Names to compare (matched title, release names).</param>
    /// <returns>The existing folder name, or <c>null</c>.</returns>
    public static string? FindExisting(IEnumerable<string> existing, string? tag, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(names);
        var folders = existing.ToList();
        if (!string.IsNullOrEmpty(tag))
        {
            var byTag = folders.FirstOrDefault(f => f.Contains("[" + tag + "]", StringComparison.OrdinalIgnoreCase));
            if (byTag is not null)
            {
                return byTag;
            }
        }

        var wanted = names.Select(Normalize).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (wanted.Count == 0)
        {
            return null;
        }

        // A folder tagged with another id is a different title with the same name.
        return folders
            .Where(f => string.IsNullOrEmpty(tag) || !HasOtherTag(f, tag))
            .FirstOrDefault(f => wanted.Contains(Normalize(f)));
    }

    /// <summary>
    /// Normalizes a title or folder name for comparison: drops "(year)" and "[tags]",
    /// punctuation and case; "ё" equals "е".
    /// </summary>
    /// <param name="value">Title or folder name.</param>
    /// <returns>Normalized text.</returns>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = Brackets().Replace(value, " ");
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c == 'ё' ? 'е' : c);
            }
            else if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().Trim();
    }

    private static bool HasOtherTag(string folder, string tag)
    {
        var kind = tag[..(tag.IndexOf('-', StringComparison.Ordinal) + 1)];
        return folder.Contains("[" + kind, StringComparison.OrdinalIgnoreCase)
            && !folder.Contains("[" + tag + "]", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^[A-Za-z0-9]{1,20}$")]
    private static partial Regex SafeId();

    [GeneratedRegex(@"(?:\b(?:сезон\w*|season)\s*:?\s*|\bS)(?<n>\d{1,3})(?<more>\s*(?:-|–|,|&|и)\s*\d)?", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonPattern();

    [GeneratedRegex(@"\([^()]*\)|\[[^\[\]]*\]")]
    private static partial Regex Brackets();
}
