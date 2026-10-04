using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.RuTracker.QBittorrent;

/// <summary>
/// Pure helpers for the qBittorrent Web API v2.
/// </summary>
internal static partial class QBittorrentProtocol
{
    /// <summary>
    /// Gets a value indicating whether the body of <c>/api/v2/auth/login</c> means success.
    /// qBittorrent answers HTTP 200 with "Ok." on success and "Fails." on wrong credentials.
    /// </summary>
    /// <param name="body">Response body.</param>
    /// <returns><c>true</c> on success.</returns>
    public static bool IsLoginAccepted(string? body)
        => string.Equals(body?.Trim(), "Ok.", StringComparison.Ordinal);

    /// <summary>
    /// Extracts the session cookie (<c>SID</c>, or a custom name in newer builds)
    /// from <c>Set-Cookie</c> headers as a ready-to-send <c>name=value</c> pair.
    /// </summary>
    /// <param name="setCookieHeaders">Raw <c>Set-Cookie</c> values.</param>
    /// <returns>The cookie pair, or <c>null</c>.</returns>
    public static string? ExtractSessionCookie(IEnumerable<string> setCookieHeaders)
    {
        ArgumentNullException.ThrowIfNull(setCookieHeaders);
        string? fallback = null;
        foreach (var header in setCookieHeaders)
        {
            var pair = header.Split(';', 2)[0].Trim();
            var match = CookiePair().Match(pair);
            if (!match.Success)
            {
                continue;
            }

            if (match.Groups["name"].Value.Contains("SID", StringComparison.OrdinalIgnoreCase))
            {
                return pair;
            }

            fallback ??= pair;
        }

        return fallback;
    }

    /// <summary>
    /// Reads the default save path from <c>/api/v2/app/preferences</c>.
    /// </summary>
    /// <param name="preferencesJson">Preferences JSON.</param>
    /// <returns>The save path, or <c>null</c>.</returns>
    public static string? ReadSavePath(string preferencesJson)
    {
        try
        {
            using var document = JsonDocument.Parse(preferencesJson);
            return document.RootElement.TryGetProperty("save_path", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A conservative cookie pair: no spaces, separators or control characters.
    [GeneratedRegex("^(?<name>[A-Za-z0-9_-]{1,64})=(?<value>[A-Za-z0-9%._~+/=-]{8,256})$")]
    private static partial Regex CookiePair();
}
