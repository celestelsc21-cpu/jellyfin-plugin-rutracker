using System;
using System.Globalization;

namespace Jellyfin.Plugin.RuTracker.Streaming;

/// <summary>
/// Parses a single HTTP <c>Range: bytes=…</c> header. Pure logic.
/// </summary>
internal static class ByteRange
{
    /// <summary>
    /// Parses the header against a file size.
    /// </summary>
    /// <param name="header">Header value, or <c>null</c>.</param>
    /// <param name="size">File size.</param>
    /// <param name="start">First byte (inclusive).</param>
    /// <param name="end">Last byte (inclusive).</param>
    /// <returns><c>true</c> for a satisfiable range or no header (whole file);
    /// <c>false</c> when the range cannot be satisfied (HTTP 416).</returns>
    public static bool TryParse(string? header, long size, out long start, out long end)
    {
        start = 0;
        end = size - 1;
        if (string.IsNullOrWhiteSpace(header))
        {
            return size > 0;
        }

        var value = header.Trim();
        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || value.Contains(',', StringComparison.Ordinal))
        {
            // Unknown unit or multiple ranges: serve the whole file.
            return size > 0;
        }

        var spec = value[6..].Trim();
        var dash = spec.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
        {
            return false;
        }

        var left = spec[..dash].Trim();
        var right = spec[(dash + 1)..].Trim();
        if (left.Length == 0)
        {
            // Suffix range: the last N bytes.
            if (!long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var suffix) || suffix <= 0 || size == 0)
            {
                return false;
            }

            start = Math.Max(0, size - suffix);
            return true;
        }

        if (!long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out start) || start >= size)
        {
            return false;
        }

        if (right.Length > 0)
        {
            if (!long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out end) || end < start)
            {
                return false;
            }

            end = Math.Min(end, size - 1);
        }

        return true;
    }
}
