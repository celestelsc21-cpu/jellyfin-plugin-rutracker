using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.RuTracker.Torrents;
using Jellyfin.Plugin.RuTracker.Tracker;

namespace Jellyfin.Plugin.RuTracker.Library;

/// <summary>
/// Builds safe file renames for episodes without changing torrent file indexes.
/// </summary>
internal static partial class EpisodeFileNaming
{
    /// <summary>
    /// Plans unambiguous episode renames without overwriting another torrent file.
    /// </summary>
    /// <param name="files">Files reported by qBittorrent.</param>
    /// <param name="season">Selected season, if any.</param>
    /// <param name="title">Matched series title.</param>
    /// <returns>Safe rename operations.</returns>
    public static IReadOnlyList<EpisodeFileRename> Plan(IEnumerable<TorrentFileEntry> files, int? season, string? title)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (string.IsNullOrWhiteSpace(title))
        {
            return [];
        }

        var safeTitle = ReleaseTitle.Sanitize(title).Trim();
        if (string.IsNullOrWhiteSpace(safeTitle))
        {
            return [];
        }

        if (safeTitle.Length > 100)
        {
            safeTitle = safeTitle[..100].TrimEnd(' ', '.');
        }

        var all = files.ToList();
        var candidates = new List<EpisodeFileRename>();
        foreach (var file in all.Where(f => EpisodePlanner.IsVideo(f.Path)))
        {
            var path = file.Path.Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(path);
            if (!TryParse(name, season, out var fileSeason, out var episode))
            {
                continue;
            }

            var separator = path.LastIndexOf('/');
            var folder = separator < 0 ? string.Empty : path[..(separator + 1)];
            var newPath = folder + safeTitle + " - S"
                + fileSeason.ToString("00", CultureInfo.InvariantCulture)
                + "E" + episode.ToString("00", CultureInfo.InvariantCulture)
                + Path.GetExtension(path);
            if (!string.Equals(path, newPath, StringComparison.Ordinal))
            {
                candidates.Add(new EpisodeFileRename(file.Index, path, newPath));
            }
        }

        var existing = all.Select(f => f.Path.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicates = candidates.GroupBy(c => c.NewPath, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates.Where(c => !existing.Contains(c.NewPath) && !duplicates.Contains(c.NewPath)).ToList();
    }

    /// <summary>
    /// Reads a reliably marked season and episode, or an episode with an explicit default season.
    /// </summary>
    /// <param name="name">File name without extension.</param>
    /// <param name="defaultSeason">Season selected for the download.</param>
    /// <param name="season">Detected season.</param>
    /// <param name="episode">Detected episode.</param>
    /// <returns>True when the numbering is unambiguous.</returns>
    public static bool TryParse(string name, int? defaultSeason, out int season, out int episode)
    {
        season = 0;
        episode = 0;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var full = SeasonEpisode().Match(name);
        if (full.Success)
        {
            if (!int.TryParse(full.Groups["s"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out season)
                || !int.TryParse(full.Groups["e"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out episode))
            {
                return false;
            }

            return season is >= 0 and <= 100 && episode is >= 1 and <= 10000
                && (defaultSeason is null || defaultSeason == season);
        }

        if (defaultSeason is null || defaultSeason < 0 || defaultSeason > 100)
        {
            return false;
        }

        var single = EpisodeNumber().Match(name);
        if (!single.Success || !int.TryParse(single.Groups["e"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out episode))
        {
            return false;
        }

        season = defaultSeason.GetValueOrDefault();
        return episode is >= 1 and <= 10000;
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:[Ss](?<s>\d{1,2})[\s._-]*[Ee](?<e>\d{1,3})|(?<s>\d{1,2})[xX](?<e>\d{1,3}))(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonEpisode();

    [GeneratedRegex(@"(?:^|[\s._-])(?:серия[\s._-]*(?<e>\d{1,3})|(?<e>\d{1,3})[\s._-]*серия)(?:$|[\s._-])", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeNumber();
}
