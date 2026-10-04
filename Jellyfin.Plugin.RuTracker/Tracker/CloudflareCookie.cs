using System;

namespace Jellyfin.Plugin.RuTracker.Tracker;

/// <summary>
/// Normalizes a Cloudflare clearance cookie copied from a browser.
/// </summary>
internal static class CloudflareCookie
{
    /// <summary>
    /// Gets the cookie value, accepting either the raw value or <c>cf_clearance=value</c>.
    /// </summary>
    /// <param name="raw">Copied cookie value.</param>
    /// <returns>Normalized value, or <c>null</c> when invalid.</returns>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();
        const string Prefix = "cf_clearance=";
        if (value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[Prefix.Length..];
        }

        value = value.Split(';', 2)[0].Trim();
        if (value.Length is < 8 or > 4096 || value.Contains('\r') || value.Contains('\n'))
        {
            return null;
        }

        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch) || ch == ';' || ch == ',')
            {
                return null;
            }
        }

        return value;
    }
}