using System;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.RuTracker.Tracker;

/// <summary>
/// Facts derived from a RuTracker topic title. Search results have no separate
/// columns for these, so uploaders' title conventions are used instead.
/// </summary>
internal static partial class TitleTags
{
    /// <summary>
    /// Gets a value indicating whether the title announces subtitles,
    /// e.g. "Dub + Sub Rus, Eng", "[2024, WEB-DL] Subs", "русские субтитры".
    /// </summary>
    /// <param name="title">Topic title.</param>
    /// <returns><c>true</c> if subtitles are mentioned.</returns>
    public static bool HasSubtitles(string? title)
        => !string.IsNullOrEmpty(title) && Subtitles().IsMatch(title);

    // "Sub"/"Subs" as whole words (not "Subway", "Substance"), or the Russian word stem.
    [GeneratedRegex(@"(?<![\p{L}\p{N}])subs?(?![\p{L}\p{N}])|субтитр", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Subtitles();
}
