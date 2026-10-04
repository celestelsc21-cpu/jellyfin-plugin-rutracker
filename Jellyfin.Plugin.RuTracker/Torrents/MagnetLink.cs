using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.RuTracker.Torrents;

/// <summary>
/// Helpers for magnet links.
/// </summary>
internal static partial class MagnetLink
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>
    /// Extracts the v1 info hash from a magnet link (hex or base32 form).
    /// </summary>
    /// <param name="magnet">Magnet URI.</param>
    /// <returns>Lowercase hex info hash, or <c>null</c>.</returns>
    public static string? GetInfoHash(string? magnet)
    {
        if (string.IsNullOrEmpty(magnet) || !magnet.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var match = BtihParameter().Match(magnet);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups[1].Value;
        if (value.Length == 40 && HexOnly().IsMatch(value))
        {
            return value.ToLowerInvariant();
        }

        return value.Length == 32 ? FromBase32(value.ToUpperInvariant()) : null;
    }

    private static string? FromBase32(string value)
    {
        var bytes = new byte[20];
        int buffer = 0, bits = 0, index = 0;
        foreach (var c in value)
        {
            var digit = Base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (digit < 0)
            {
                return null;
            }

            buffer = (buffer << 5) | digit;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes[index++] = (byte)(buffer >> bits);
                buffer &= (1 << bits) - 1;
            }
        }

        var hex = new StringBuilder(40);
        foreach (var b in bytes)
        {
            hex.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }

    [GeneratedRegex(@"[?&]xt=urn:btih:([A-Za-z0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BtihParameter();

    [GeneratedRegex("^[0-9A-Fa-f]{40}$")]
    private static partial Regex HexOnly();
}
