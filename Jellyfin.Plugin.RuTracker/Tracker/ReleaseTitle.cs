using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.RuTracker.Tracker;

/// <summary>
/// Parses RuTracker topic titles of the usual form
/// "Русское / Original (Режиссёр) [2006, США, драма, BDRip 1080p] Dub + Sub" or
/// "Сериал / Show / Сезон: 1 / Серии: 1-10 из 10 (Студия) [2024, WEB-DL]".
/// </summary>
internal static partial class ReleaseTitle
{
    private const int MaxFolderNameLength = 100;

    /// <summary>
    /// Extracts the titles and the year.
    /// </summary>
    /// <param name="title">Topic title.</param>
    /// <returns>Titles in their original order (Russian first, original last) and the year.</returns>
    public static (IReadOnlyList<string> Names, int? Year) Parse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return ([], null);
        }

        var bracket = title.IndexOf('[', StringComparison.Ordinal);
        var head = bracket >= 0 ? title[..bracket] : title;

        // Drop trailing "(Director / Studio)" groups.
        head = TrailingParentheses().Replace(head, string.Empty);

        var names = head.Split(" / ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(n => !SeasonOrEpisodes().IsMatch(n))
            .Select(n => Spaces().Replace(n, " ").Trim())
            .Where(n => n.Length > 0)
            .ToList();

        int? year = null;
        if (bracket >= 0)
        {
            var match = Year().Match(title, bracket);
            if (match.Success)
            {
                year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        return (names, year);
    }

    /// <summary>
    /// Builds a Jellyfin-friendly folder name "Original Title (2006)", safe on Windows.
    /// The original (Latin) title is preferred: it matches online metadata most reliably.
    /// </summary>
    /// <param name="title">Topic title.</param>
    /// <param name="fallback">Name used when nothing can be parsed.</param>
    /// <returns>Folder name.</returns>
    public static string FolderName(string? title, string fallback)
    {
        var (names, year) = Parse(title);
        var name = names.LastOrDefault(n => n.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
            ?? names.FirstOrDefault()
            ?? fallback;

        var folder = Sanitize(name);
        if (folder.Length == 0)
        {
            folder = Sanitize(fallback);
        }

        if (folder.Length > MaxFolderNameLength)
        {
            folder = folder[..MaxFolderNameLength].TrimEnd(' ', '.');
        }

        return year is null ? folder : folder + " (" + year.Value.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Makes a string safe as a single Windows/Linux path segment.
    /// </summary>
    /// <param name="value">Value.</param>
    /// <returns>Sanitized value (may be empty).</returns>
    public static string Sanitize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c < 32 || "<>:\"/\\|?*".Contains(c, StringComparison.Ordinal) ? ' ' : c);
        }

        var result = Spaces().Replace(builder.ToString(), " ").Trim().TrimEnd('.', ' ');
        return result is "." or ".." ? string.Empty : result;
    }

    [GeneratedRegex(@"(\s*\([^()]*\))+\s*$")]
    private static partial Regex TrailingParentheses();

    [GeneratedRegex(@"^(сезон|серии|серия|выпуск|выпуски|season|episodes?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonOrEpisodes();

    [GeneratedRegex(@"\b(19\d{2}|20\d{2})\b")]
    private static partial Regex Year();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
