using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.RuTracker.Configuration;

/// <summary>
/// Pure path checks for both sides of a <see cref="DownloadTarget"/>.
/// The plugin runs on Linux, so Windows paths are validated as plain strings
/// and never passed to <see cref="System.IO.Path"/>.
/// </summary>
internal static partial class PathRules
{
    /// <summary>
    /// Checks that the value is an absolute Windows path: <c>X:\...</c> or <c>\\server\share\...</c>,
    /// without relative segments or characters invalid on Windows.
    /// </summary>
    /// <param name="path">Path to check.</param>
    /// <returns><c>true</c> if the path is acceptable.</returns>
    public static bool IsAbsoluteWindowsPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 240)
        {
            return false;
        }

        var normalized = path.Replace('/', '\\');
        string rest;
        if (DriveRoot().IsMatch(normalized))
        {
            rest = normalized[3..];
        }
        else if (normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var unc = normalized[2..].Split('\\');
            if (unc.Length < 2 || unc[0].Length == 0 || unc[1].Length == 0)
            {
                return false;
            }

            rest = string.Join('\\', unc);
        }
        else
        {
            return false;
        }

        return rest.Split('\\', StringSplitOptions.RemoveEmptyEntries).All(IsValidWindowsSegment);
    }

    /// <summary>
    /// Checks that the value is an absolute POSIX path without relative segments.
    /// </summary>
    /// <param name="path">Path to check.</param>
    /// <returns><c>true</c> if the path is acceptable.</returns>
    public static bool IsAbsoluteLinuxPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path[0] != '/' || path.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        return path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .All(s => s != "." && s != "..");
    }

    private static bool IsValidWindowsSegment(string segment)
    {
        if (segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' '))
        {
            return false;
        }

        return segment.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) < 0
            && segment.All(c => c >= 32);
    }

    [GeneratedRegex(@"^[A-Za-z]:\\")]
    private static partial Regex DriveRoot();
}
