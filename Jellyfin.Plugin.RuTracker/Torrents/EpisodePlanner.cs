using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.RuTracker.Torrents;

/// <summary>
/// Decides in which order files are watched and how qBittorrent should prioritise them.
/// The whole torrent is always downloaded; only the order changes.
/// </summary>
internal static partial class EpisodePlanner
{
    /// <summary>
    /// qBittorrent priority: normal.
    /// </summary>
    public const int PriorityNormal = 1;

    /// <summary>
    /// qBittorrent priority: high (the episode after the one being watched).
    /// </summary>
    public const int PriorityHigh = 6;

    /// <summary>
    /// qBittorrent priority: maximal (the episode being watched).
    /// </summary>
    public const int PriorityMaximal = 7;

    private static readonly string[] VideoExtensions =
        [".mkv", ".mp4", ".avi", ".m4v", ".ts", ".m2ts", ".mts", ".mov", ".wmv", ".webm", ".mpg", ".mpeg", ".vob", ".flv"];

    /// <summary>
    /// Gets a value indicating whether the path is a video file.
    /// </summary>
    /// <param name="path">File path.</param>
    /// <returns><c>true</c> for video files.</returns>
    public static bool IsVideo(string path)
        => VideoExtensions.Contains(Path.GetExtension(path ?? string.Empty), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Video files in natural order ("Серия 2" before "Серия 10"; season folders respected).
    /// </summary>
    /// <param name="files">Torrent files.</param>
    /// <returns>Video files in watching order.</returns>
    public static IReadOnlyList<TorrentFileEntry> VideosInOrder(IEnumerable<TorrentFileEntry> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return files.Where(f => IsVideo(f.Path)).OrderBy(f => f.Path, NaturalComparer.Instance).ToList();
    }

    /// <summary>
    /// Builds the watching order: the chosen file first, then the following ones,
    /// then those before it (the whole torrent is downloaded anyway).
    /// </summary>
    /// <param name="files">Torrent files.</param>
    /// <param name="startFileIndex">File to start with; <c>null</c> means the first episode.</param>
    /// <returns>File indexes of video files in watching order.</returns>
    public static IReadOnlyList<int> BuildOrder(IEnumerable<TorrentFileEntry> files, int? startFileIndex)
    {
        var videos = VideosInOrder(files).Select(f => f.Index).ToList();
        var start = startFileIndex is null ? -1 : videos.IndexOf(startFileIndex.Value);
        if (start <= 0)
        {
            return videos;
        }

        return videos.Skip(start).Concat(videos.Take(start)).ToList();
    }

    /// <summary>
    /// Computes qBittorrent file priorities: the first unfinished episode in watching order
    /// gets maximal priority, the next one high, everything else normal. Companion files
    /// (external audio tracks, subtitles named after the episode) follow their episode.
    /// Nothing is skipped.
    /// </summary>
    /// <param name="files">Torrent files.</param>
    /// <param name="order">Watching order (file indexes).</param>
    /// <param name="isComplete">Tells whether a file has finished downloading.</param>
    /// <returns>File index to priority.</returns>
    public static IReadOnlyDictionary<int, int> Priorities(
        IReadOnlyList<TorrentFileEntry> files,
        IReadOnlyList<int> order,
        Func<int, bool> isComplete)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(isComplete);

        var result = files.ToDictionary(f => f.Index, _ => PriorityNormal);
        var pending = order.Where(i => !isComplete(i)).Take(2).ToList();
        for (var rank = 0; rank < pending.Count; rank++)
        {
            var priority = rank == 0 ? PriorityMaximal : PriorityHigh;
            var episode = files.FirstOrDefault(f => f.Index == pending[rank]);
            if (episode is null)
            {
                continue;
            }

            result[episode.Index] = priority;
            foreach (var companion in Companions(files, episode))
            {
                result[companion.Index] = Math.Max(result[companion.Index], priority);
            }
        }

        return result;
    }

    /// <summary>
    /// Short display label for a file: "S01E02", "Серия 5" or the file name.
    /// </summary>
    /// <param name="path">File path inside the torrent.</param>
    /// <returns>Label.</returns>
    public static string Label(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path ?? string.Empty);
        var se = SeasonEpisode().Match(name);
        if (se.Success)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"S{int.Parse(se.Groups[1].Value, CultureInfo.InvariantCulture):00}E{int.Parse(se.Groups[2].Value, CultureInfo.InvariantCulture):00} — {name}");
        }

        return name;
    }

    private static IEnumerable<TorrentFileEntry> Companions(IReadOnlyList<TorrentFileEntry> files, TorrentFileEntry episode)
    {
        var stem = Path.GetFileNameWithoutExtension(episode.Path);
        if (stem.Length < 3)
        {
            return [];
        }

        return files.Where(f => f.Index != episode.Index
            && !IsVideo(f.Path)
            && Path.GetFileName(f.Path).StartsWith(stem, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"[Ss](\d{1,2})[ ._-]?[EeЕе](\d{1,3})")]
    private static partial Regex SeasonEpisode();

    /// <summary>
    /// Compares strings treating digit runs as numbers.
    /// </summary>
    private sealed partial class NaturalComparer : IComparer<string>
    {
        public static NaturalComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            var a = Chunks().Matches(x ?? string.Empty);
            var b = Chunks().Matches(y ?? string.Empty);
            for (var i = 0; i < Math.Min(a.Count, b.Count); i++)
            {
                var ca = a[i].Value;
                var cb = b[i].Value;
                int result;
                if (char.IsDigit(ca[0]) && char.IsDigit(cb[0]))
                {
                    var na = ca.TrimStart('0');
                    var nb = cb.TrimStart('0');
                    result = na.Length != nb.Length
                        ? na.Length.CompareTo(nb.Length)
                        : string.CompareOrdinal(na, nb);
                }
                else
                {
                    result = string.Compare(ca, cb, StringComparison.OrdinalIgnoreCase);
                }

                if (result != 0)
                {
                    return result;
                }
            }

            return a.Count.CompareTo(b.Count);
        }

        [GeneratedRegex(@"\d+|\D+")]
        private static partial Regex Chunks();
    }
}
