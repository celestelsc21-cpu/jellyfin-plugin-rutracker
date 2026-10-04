using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.RuTracker.Configuration;

/// <summary>
/// Translates paths between the Jellyfin container (POSIX) and the
/// qBittorrent machine (Windows) using prefix mappings. Longest prefix wins.
/// </summary>
internal sealed class PathMapper
{
    private readonly IReadOnlyList<(string Local, string Remote)> _mappings;

    /// <summary>
    /// Initializes a new instance of the <see cref="PathMapper"/> class.
    /// Invalid mappings are ignored (the validator reports them separately).
    /// </summary>
    /// <param name="mappings">Configured mappings.</param>
    public PathMapper(IEnumerable<PathMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        _mappings = mappings
            .Where(m => PathRules.IsAbsoluteLinuxPath(m.JellyfinPrefix) && PathRules.IsAbsoluteWindowsPath(m.QBittorrentPrefix))
            .Select(m => (Local: NormalizeLocal(m.JellyfinPrefix), Remote: NormalizeRemote(m.QBittorrentPrefix)))
            .ToList();
    }

    /// <summary>
    /// Converts a Jellyfin path to the qBittorrent (Windows) path.
    /// </summary>
    /// <param name="jellyfinPath">Absolute POSIX path inside the container.</param>
    /// <param name="qbittorrentPath">Resulting Windows path.</param>
    /// <returns><c>true</c> if a mapping covers the path.</returns>
    public bool TryToQBittorrent(string jellyfinPath, out string qbittorrentPath)
    {
        qbittorrentPath = string.Empty;
        if (!PathRules.IsAbsoluteLinuxPath(jellyfinPath))
        {
            return false;
        }

        var local = NormalizeLocal(jellyfinPath);
        var match = _mappings
            .Where(m => IsUnder(local, m.Local, '/', StringComparison.Ordinal))
            .OrderByDescending(m => m.Local.Length)
            .FirstOrDefault();
        if (match.Local is null)
        {
            return false;
        }

        var tail = local[match.Local.TrimEnd('/').Length..].TrimStart('/');
        qbittorrentPath = tail.Length == 0 ? match.Remote : match.Remote.TrimEnd('\\') + "\\" + tail.Replace('/', '\\');
        return true;
    }

    /// <summary>
    /// Converts a qBittorrent (Windows) path to the Jellyfin path.
    /// </summary>
    /// <param name="qbittorrentPath">Absolute Windows path reported by qBittorrent.</param>
    /// <param name="jellyfinPath">Resulting POSIX path.</param>
    /// <returns><c>true</c> if a mapping covers the path.</returns>
    public bool TryToJellyfin(string qbittorrentPath, out string jellyfinPath)
    {
        jellyfinPath = string.Empty;
        if (!PathRules.IsAbsoluteWindowsPath(qbittorrentPath))
        {
            return false;
        }

        var remote = NormalizeRemote(qbittorrentPath);
        var match = _mappings
            .Where(m => IsUnder(remote, m.Remote, '\\', StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.Remote.Length)
            .FirstOrDefault();
        if (match.Remote is null)
        {
            return false;
        }

        var tail = remote[match.Remote.TrimEnd('\\').Length..].TrimStart('\\');
        jellyfinPath = tail.Length == 0 ? match.Local : match.Local.TrimEnd('/') + "/" + tail.Replace('\\', '/');
        return true;
    }

    private static bool IsUnder(string path, string prefix, char separator, StringComparison comparison)
    {
        var p = prefix.TrimEnd(separator);
        return path.Equals(p, comparison)
            || path.StartsWith(p + separator, comparison)
            || (p.Length == 0 && path.Length > 0); // POSIX root "/"
    }

    private static string NormalizeLocal(string path)
    {
        var trimmed = path.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    private static string NormalizeRemote(string path)
    {
        var p = path.Replace('/', '\\');
        // Keep "C:\" for a drive root, strip trailing separators otherwise.
        return p.Length == 3 && p[1] == ':' ? p : p.TrimEnd('\\');
    }
}
