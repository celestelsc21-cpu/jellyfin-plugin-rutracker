using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.RuTracker.Tracker;

/// <summary>
/// Minimal cookie handling for requests made with <c>UseCookies = false</c>:
/// collects cookies a page sets and builds the <c>Cookie</c> header for the next request.
/// </summary>
internal static partial class CookieJar
{
    /// <summary>
    /// Collects cookies from <c>Set-Cookie</c> headers. Deleted, empty or unsafe values are skipped.
    /// </summary>
    /// <param name="setCookieHeaders">Raw <c>Set-Cookie</c> values.</param>
    /// <returns>Cookie name to value.</returns>
    public static Dictionary<string, string> Collect(IEnumerable<string> setCookieHeaders)
    {
        ArgumentNullException.ThrowIfNull(setCookieHeaders);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var header in setCookieHeaders)
        {
            var pair = header.Split(';', 2)[0].Trim();
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var name = pair[..separator];
            var value = pair[(separator + 1)..];
            if (value.Length == 0 || value.Equals("deleted", StringComparison.Ordinal))
            {
                result.Remove(name);
                continue;
            }

            if (SafeName().IsMatch(name) && SafeValue().IsMatch(value))
            {
                result[name] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Builds a <c>Cookie</c> header value.
    /// </summary>
    /// <param name="cookies">Cookies to send.</param>
    /// <returns>Header value, or <c>null</c> when there is nothing to send.</returns>
    public static string? ToHeader(IReadOnlyDictionary<string, string> cookies)
    {
        ArgumentNullException.ThrowIfNull(cookies);
        return cookies.Count == 0 ? null : string.Join("; ", cookies.Select(c => c.Key + "=" + c.Value));
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,64}$")]
    private static partial Regex SafeName();

    [GeneratedRegex("^[A-Za-z0-9%._~+/=:-]{1,512}$")]
    private static partial Regex SafeValue();
}
