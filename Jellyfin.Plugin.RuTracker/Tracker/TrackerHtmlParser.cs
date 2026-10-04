using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Jellyfin.Plugin.RuTracker.Tracker;

/// <summary>
/// Parses RuTracker HTML pages. Pure functions, no I/O.
/// </summary>
/// <remarks>
/// Selectors follow the long-maintained Jackett RuTracker indexer. Each row is
/// parsed independently so one malformed row never breaks the whole page.
/// </remarks>
internal static partial class TrackerHtmlParser
{
    /// <summary>
    /// Gets a value indicating whether the page was rendered for a logged-in user.
    /// </summary>
    /// <param name="html">Page HTML.</param>
    /// <returns><c>true</c> if logged in.</returns>
    public static bool IsLoggedIn(string html)
        => html.Contains("id=\"logged-in-username\"", StringComparison.Ordinal);

    /// <summary>
    /// Gets a value indicating whether the page asks for a captcha (login throttling).
    /// </summary>
    /// <param name="html">Page HTML.</param>
    /// <returns><c>true</c> if a captcha is required.</returns>
    public static bool HasCaptcha(string html)
        => html.Contains("name=\"cap_sid\"", StringComparison.Ordinal);

    /// <summary>
    /// Parses the result table of <c>tracker.php</c>.
    /// </summary>
    /// <param name="html">Page HTML.</param>
    /// <returns>Parsed torrents; rows that cannot be parsed are skipped.</returns>
    public static IReadOnlyList<TorrentInfo> ParseSearchResults(string html)
    {
        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);
        var result = new List<TorrentInfo>();

        foreach (var row in document.QuerySelectorAll("table#tor-tbl > tbody > tr"))
        {
            var item = ParseRow(row);
            if (item is not null)
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static TorrentInfo? ParseRow(IElement row)
    {
        var titleLink = row.QuerySelector("td.t-title-col div.t-title a.tLink");
        if (titleLink is null)
        {
            return null; // "nothing found" row or layout change
        }

        var topicId = ParseLong(titleLink.GetAttribute("data-topic_id"))
            ?? ParseLong(QueryValue(titleLink.GetAttribute("href"), "t"));
        if (topicId is null or <= 0)
        {
            return null;
        }

        var forumLink = row.QuerySelector("td.f-name-col div.f-name a");
        var forumId = (int)(ParseLong(QueryValue(forumLink?.GetAttribute("href"), "f")) ?? 0);
        var forumName = Clean(forumLink?.TextContent);

        var seedersCell = row.QuerySelector("td:nth-child(7)");
        var seeders = seedersCell is null || seedersCell.TextContent.Contains("дн", StringComparison.Ordinal)
            ? 0
            : ParseInt(seedersCell.QuerySelector("b")?.TextContent);

        var addedUnix = ParseLong(row.QuerySelector("td:nth-child(10)")?.GetAttribute("data-ts_text"));

        return new TorrentInfo(
            topicId.Value,
            Clean(titleLink.TextContent),
            forumId,
            forumName,
            ForumClassifier.Classify(forumId, forumName),
            ParseLong(row.QuerySelector("td.tor-size")?.GetAttribute("data-ts_text")) ?? 0,
            seeders,
            ParseInt(row.QuerySelector("td:nth-child(8)")?.TextContent),
            ParseInt(row.QuerySelector("td:nth-child(9)")?.TextContent),
            addedUnix is > 0 ? DateTimeOffset.FromUnixTimeSeconds(addedUnix.Value) : null,
            Clean(row.QuerySelector("td.u-name-col")?.TextContent));
    }

    private static string? QueryValue(string? href, string name)
    {
        if (string.IsNullOrEmpty(href))
        {
            return null;
        }

        var query = href.Contains('?', StringComparison.Ordinal) ? href[(href.IndexOf('?', StringComparison.Ordinal) + 1)..] : href;
        return query.Split('&')
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2 && p[0] == name)
            .Select(p => p[1])
            .FirstOrDefault();
    }

    private static long? ParseLong(string? value)
    {
        var digits = DigitsOnly(value);
        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static int ParseInt(string? value)
    {
        var digits = DigitsOnly(value);
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    private static string DigitsOnly(string? value)
        => string.IsNullOrEmpty(value) ? string.Empty : NonDigits().Replace(value, string.Empty);

    private static string Clean(string? text)
        => string.IsNullOrWhiteSpace(text) ? string.Empty : Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"[^0-9]")]
    private static partial Regex NonDigits();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
