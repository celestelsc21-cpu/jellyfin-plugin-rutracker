using System;
using System.IO;

namespace Jellyfin.Plugin.RuTracker.Library;

/// <summary>
/// Combines a folder with a path taken from a torrent, refusing anything that would
/// escape the folder (torrent file names are untrusted input).
/// </summary>
internal static class SafePath
{
    /// <summary>
    /// Combines <paramref name="root"/> and a relative path from a torrent.
    /// </summary>
    /// <param name="root">Absolute folder.</param>
    /// <param name="relative">Relative path (forward or back slashes).</param>
    /// <returns>Full path inside <paramref name="root"/>, or <c>null</c> when it would escape.</returns>
    public static string? Combine(string root, string relative)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\0', StringComparison.Ordinal))
        {
            return null;
        }

        var parts = relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part is "." or "..")
            {
                return null;
            }
        }

        var fullRoot = Path.GetFullPath(root).TrimEnd('/');
        var full = Path.GetFullPath(Path.Combine([fullRoot, .. parts]));
        return full.StartsWith(fullRoot + "/", StringComparison.Ordinal) ? full : null;
    }
}
