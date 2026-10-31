using System;
using System.Globalization;
using System.Net.Http;

namespace Jellyfin.Plugin.RuTracker.Tracker;

/// <summary>
/// Classifies Cloudflare challenge responses without exposing cookie values.
/// </summary>
internal static class CloudflareDiagnostics
{
    /// <summary>
    /// Detects challenges using a response header or known HTML markers.
    /// </summary>
    /// <param name="response">The HTTP response.</param>
    /// <param name="html">The response HTML, if available.</param>
    /// <returns>Diagnostic text or null for an ordinary response.</returns>
    public static string? Detect(HttpResponseMessage response, string html)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(html);

        var mitigated = false;
        if (response.Headers.TryGetValues("cf-mitigated", out var values))
        {
            foreach (var value in values)
            {
                mitigated |= string.Equals(value, "challenge", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (!mitigated && !TrackerHtmlParser.IsCloudflareChallenge(html))
        {
            return null;
        }

        var type = html.Contains("challenges.cloudflare.com/turnstile", StringComparison.OrdinalIgnoreCase)
            ? "Turnstile"
            : html.Contains("cf-chl-", StringComparison.OrdinalIgnoreCase)
                ? "JavaScript/Managed Challenge"
                : "unknown type";
        return "Cloudflare Challenge (HTTP "
            + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
            + (mitigated ? ", cf-mitigated: challenge" : ", HTML challenge")
            + ", " + type + ")";
    }
}
