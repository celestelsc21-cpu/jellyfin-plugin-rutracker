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
        => html.Contains("name=\"cap_sid\"", StringComparison.Ordinal)
           || html.Contains("name=\"cap_code_", StringComparison.Ordinal)
           || html.Contains("static.rutracker.cc/captcha", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a value indicating whether the page is a Cloudflare challenge page.
    /// </summary>
    /// <param name="html">Page HTML.</param>
    /// <returns><c>true</c> if the page is a Cloudflare challenge.</returns>
    public static bool IsCloudflareChallenge(string html)
        => html.Contains("<title>Just a moment...</title>", StringComparison.OrdinalIgnoreCase)
           || html.Contains("challenges.cloudflare.com", StringComparison.OrdinalIgnoreCase)
           || html.Contains("cf-chl-", StringComparison.OrdinalIgnoreCase)
           || html.Contains("Enable JavaScript and cookies to continue", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Collects hidden fields of the login form (for example <c>redirect</c> or a form token)
    /// so they can be sent back with the credentials, like a browser does.
    /// Captcha fields are skipped: they cannot be answered automatically.
    /// </summary>
    /// <param name="html">Login page HTML.</param>
    /// <returns>Field name to value.</returns>
    public static IReadOnlyList<KeyValuePair<string, string>> ExtractLoginHiddenFields(string html)
    {
        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);
        var form = document.QuerySelectorAll("form")
            .FirstOrDefault(f => f.QuerySelector("input[name=login_username]") is not null);
        if (form is null)
        {
            return [];
        }

        return form.QuerySelectorAll("input[type=hidden]")
            .Select(i => new KeyValuePair<string, string>(i.GetAttribute("name") ?? string.Empty, i.GetAttribute("value") ?? string.Empty))
            .Where(f => f.Key.Length > 0
                && !f.Key.StartsWith("cap_", StringComparison.Ordinal)
                && f.Key is not "login_username" and not "login_password" and not "login")
            .ToList();
    }

    /// <summary>
    /// Gets a short piece of the visible page text, for diagnostics.
    /// </summary>
    /// <param name="html">Page HTML.</param>
    /// <param name="maxLength">Maximum length.</param>
    /// <returns>Visible text, shortened.</returns>
    public static string ExtractVisibleText(string html, int maxLength)
    {
        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);
        foreach (var node in document.QuerySelectorAll("script, style, noscript").ToList())
        {
            node.Remove();
        }

        var text = Clean(document.Body?.TextContent);
        return text.Length > maxLength ? text[..maxLength] + "…" : text;
    }

    /// <summary>
    /// Gets a value indicating whether the page comes from RuTracker at all.
    /// ISP block pages and captive portals answer 200 with unrelated content.
    /// </summary>
    /// <param name="html">Page HTML.</param>
    /// <returns><c>true</c> if the page looks like a RuTracker page.</returns>
    public static bool LooksLikeRuTracker(string html)
        => html.Contains("rutracker", StringComparison.OrdinalIgnoreCase)
           || html.Contains("bb_session", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Extracts the page title, shortened for display.
    /// </summary>
    /// <param name="html">Page HTML.</param>
    /// <returns>The title, or an empty string.</returns>
    public static string ExtractTitle(string html)
    {
        var match = TitleTag().Match(html);
        if (!match.Success)
        {
            return string.Empty;
        }

        var title = Clean(System.Net.WebUtility.HtmlDecode(match.Groups[1].Value));
        return title.Length > 80 ? title[..80] + "…" : title;
    }

    /// <summary>
    /// Extracts the error message RuTracker shows on a failed login (wrong password, ban, ...).
    /// </summary>
    /// <param name="html">Login response HTML.</param>
    /// <returns>The message, or <c>null</c>.</returns>
    public static string? ExtractLoginError(string html)
    {
        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);
        var text = Clean(document.QuerySelector("h4.warnColor1, div.msg-main")?.TextContent);
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length > 200 ? text[..200] + "…" : text;
    }

    /// <summary>
    /// Extracts the magnet link from a topic page.
    /// </summary>
    /// <param name="html">Topic page HTML.</param>
    /// <returns>The magnet URI, or <c>null</c>.</returns>
    public static string? ExtractMagnet(string html)
    {
        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);
        var href = document.QuerySelector("a.magnet-link[href^='magnet:?']")?.GetAttribute("href")
            ?? document.QuerySelector("a[href^='magnet:?']")?.GetAttribute("href");
        return string.IsNullOrWhiteSpace(href) ? null : href.Trim();
    }

    /// <summary>
    /// Extracts the topic title from a topic page.
    /// </summary>
    /// <param name="html">Topic page HTML.</param>
    /// <returns>The title, or an empty string.</returns>
    public static string ExtractTopicTitle(string html)
    {
        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);
        return Clean(document.QuerySelector("#topic-title, h1.maintitle")?.TextContent);
    }

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

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleTag();
}
