using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.RuTracker.Configuration;

namespace Jellyfin.Plugin.RuTracker.Tracker;

/// <summary>
/// Resolves the configured RuTracker addresses. Pure logic.
/// </summary>
internal static class RuTrackerUrls
{
    /// <summary>
    /// Gets the valid configured base addresses: main first, then the mirror.
    /// Invalid or duplicate entries are skipped.
    /// </summary>
    /// <param name="config">Plugin configuration.</param>
    /// <returns>Base URIs ending with a slash.</returns>
    public static IReadOnlyList<Uri> GetBaseUris(PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var result = new List<Uri>(2);
        foreach (var url in new[] { config.RuTrackerBaseUrl, config.RuTrackerMirrorUrl })
        {
            if (!ConfigurationValidator.IsAllowedRuTrackerUrl(url))
            {
                continue;
            }

            var uri = new Uri(url!.TrimEnd('/') + "/");
            if (!result.Exists(u => u.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(uri);
            }
        }

        return result;
    }

    /// <summary>
    /// Puts the address that worked last time first.
    /// </summary>
    /// <param name="bases">Configured bases in priority order.</param>
    /// <param name="preferredHost">Host that worked last, or <c>null</c>.</param>
    /// <returns>Reordered list.</returns>
    public static IReadOnlyList<Uri> PreferHost(IReadOnlyList<Uri> bases, string? preferredHost)
    {
        ArgumentNullException.ThrowIfNull(bases);
        if (preferredHost is null)
        {
            return bases;
        }

        return bases
            .OrderBy(u => u.Host.Equals(preferredHost, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();
    }

    /// <summary>
    /// Builds the relative search URL. RuTracker expects the query string in UTF-8
    /// (pages themselves are windows-1251); results are ordered by seeders, descending.
    /// </summary>
    /// <param name="normalizedQuery">Query after <see cref="SearchQuery.Normalize"/>.</param>
    /// <returns>Relative URL of <c>tracker.php</c>.</returns>
    public static string SearchPath(string normalizedQuery)
    {
        ArgumentNullException.ThrowIfNull(normalizedQuery);
        return "forum/tracker.php?nm=" + Uri.EscapeDataString(normalizedQuery) + "&o=10&s=2";
    }

    /// <summary>
    /// Checks that a redirect target stays on an allowed RuTracker host over https.
    /// </summary>
    /// <param name="target">Absolute redirect target.</param>
    /// <returns><c>true</c> if the redirect may be followed.</returns>
    public static bool IsAllowedRedirect(Uri target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.IsAbsoluteUri
            && ConfigurationValidator.IsAllowedRuTrackerUrl(target.GetLeftPart(UriPartial.Authority));
    }
}
