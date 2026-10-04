using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.RuTracker.Tracker;

/// <summary>
/// Handling of the RuTracker <c>bb_session</c> cookie.
/// </summary>
internal static partial class SessionCookie
{
    /// <summary>
    /// Cookie name used by RuTracker for the login session.
    /// </summary>
    public const string Name = "bb_session";

    /// <summary>
    /// Accepts either a bare value or <c>bb_session=value;</c> as pasted from a browser.
    /// Rejects anything that could inject extra headers or cookies.
    /// </summary>
    /// <param name="raw">User-supplied cookie.</param>
    /// <returns>Cookie value, or <c>null</c> if empty or invalid.</returns>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();
        if (value.StartsWith(Name + "=", StringComparison.OrdinalIgnoreCase))
        {
            value = value[(Name.Length + 1)..];
        }

        value = value.TrimEnd(';').Trim();
        return SafeValue().IsMatch(value) ? value : null;
    }

    /// <summary>
    /// Extracts the session value from <c>Set-Cookie</c> headers.
    /// </summary>
    /// <param name="setCookieHeaders">Raw <c>Set-Cookie</c> header values.</param>
    /// <returns>Session value, or <c>null</c> when absent or deleted.</returns>
    public static string? FromSetCookie(IEnumerable<string> setCookieHeaders)
    {
        ArgumentNullException.ThrowIfNull(setCookieHeaders);
        var header = setCookieHeaders.FirstOrDefault(h => h.TrimStart().StartsWith(Name + "=", StringComparison.Ordinal));
        if (header is null)
        {
            return null;
        }

        var value = header.TrimStart()[(Name.Length + 1)..].Split(';', 2)[0].Trim();
        return value.Length == 0 || value.Equals("deleted", StringComparison.Ordinal) ? null : Normalize(value);
    }

    [GeneratedRegex("^[A-Za-z0-9%._~+/=-]{8,512}$")]
    private static partial Regex SafeValue();
}
