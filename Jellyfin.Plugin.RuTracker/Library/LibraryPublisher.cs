using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RuTracker.Downloads;
using Jellyfin.Plugin.RuTracker.QBittorrent;
using Jellyfin.Plugin.RuTracker.Torrents;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RuTracker.Library;

/// <summary>
/// Makes finished episodes appear in the Jellyfin library one by one, and keeps
/// unfinished ones out of it.
/// </summary>
/// <remarks>
/// Download into library: a <c>.ignore</c> file (gitignore syntax, Jellyfin 10.11+) in the
/// download folder lists unfinished files; it shrinks as files complete and is removed at the end.
/// Copy mode: each finished episode (with its audio/subtitle companions) is copied from the
/// staging folder into the library. In both modes Jellyfin is told to rescan the episode.
/// File system errors are logged and retried on the next pass; they never stop the download.
/// </remarks>
internal sealed class LibraryPublisher
{
    private const string PartSuffix = ".rutracker-part";

    private readonly ILibraryMonitor _libraryMonitor;
    private readonly ILogger<LibraryPublisher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryPublisher"/> class.
    /// </summary>
    /// <param name="libraryMonitor">Library monitor.</param>
    /// <param name="logger">Logger.</param>
    public LibraryPublisher(ILibraryMonitor libraryMonitor, ILogger<LibraryPublisher> logger)
    {
        _libraryMonitor = libraryMonitor;
        _logger = logger;
    }

    /// <summary>
    /// Hides every listed file before the download starts (download-into-library mode).
    /// </summary>
    /// <param name="folder">Download folder (Jellyfin path).</param>
    /// <param name="files">Torrent files.</param>
    public void HideBeforeStart(string folder, IEnumerable<TorrentFileEntry> files)
    {
        try
        {
            Directory.CreateDirectory(folder);
            WriteIgnore(folder, IgnoreFile.Build(files.Select(f => f.Path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not prepare the download folder; unfinished files may appear in the library");
        }
    }

    /// <summary>
    /// Publishes newly finished episodes.
    /// </summary>
    /// <param name="record">Download (its <see cref="DownloadRecord.Published"/> list is updated).</param>
    /// <param name="files">Current files from qBittorrent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when the record changed.</returns>
    public async Task<bool> PublishAsync(DownloadRecord record, IReadOnlyList<QbFile> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(files);

        // Only downloads with their own "Title (Year)" folder (0.4.0+) manage a .ignore file;
        // older ones were placed directly in the library root.
        if (!record.CopyToLibrary && !string.IsNullOrEmpty(record.LibraryFolder))
        {
            try
            {
                WriteIgnore(record.JellyfinFolder, IgnoreFile.Build(files.Where(f => f.Progress < 1).Select(f => f.Name)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not update the .ignore file of download {Id}", record.Id);
            }
        }

        var changed = false;
        var entries = files.Select(f => new TorrentFileEntry(f.Index, f.Name, f.Size)).ToList();
        foreach (var video in files.Where(f => f.Progress >= 1 && EpisodePlanner.IsVideo(f.Name) && !record.Published.Contains(f.Index)))
        {
            var target = SafePath.Combine(record.EffectiveLibraryFolder, video.Name);
            if (target is null)
            {
                _logger.LogWarning("Skipping unsafe file name in download {Id}", record.Id);
                record.Published.Add(video.Index);
                changed = true;
                continue;
            }

            if (record.CopyToLibrary)
            {
                var companions = EpisodePlanner.Companions(entries, entries.First(e => e.Index == video.Index))
                    .Where(c => files.Any(f => f.Index == c.Index && f.Progress >= 1));
                if (!await CopyAsync(record, video.Name, cancellationToken).ConfigureAwait(false))
                {
                    continue; // retried on the next pass
                }

                foreach (var companion in companions)
                {
                    await CopyAsync(record, companion.Path, cancellationToken).ConfigureAwait(false);
                }
            }

            _libraryMonitor.ReportFileSystemChanged(target);
            record.Published.Add(video.Index);
            changed = true;
            _logger.LogInformation("Episode {File} of download {Id} is now in the library", Path.GetFileName(video.Name), record.Id);
        }

        return changed;
    }

    private static void WriteIgnore(string folder, string? content)
    {
        var path = Path.Combine(folder, IgnoreFile.FileName);
        var existing = File.Exists(path) ? File.ReadAllText(path) : null;
        if (existing is not null && !IgnoreFile.IsManaged(existing))
        {
            return; // somebody else's .ignore: never touch it
        }

        if (content is null)
        {
            if (existing is not null)
            {
                File.Delete(path);
            }

            return;
        }

        if (string.Equals(existing, content, StringComparison.Ordinal))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        File.WriteAllText(path, content);
    }

    private async Task<bool> CopyAsync(DownloadRecord record, string relative, CancellationToken cancellationToken)
    {
        var source = SafePath.Combine(record.JellyfinFolder, relative);
        var target = SafePath.Combine(record.EffectiveLibraryFolder, relative);
        if (source is null || target is null)
        {
            return false;
        }

        try
        {
            if (File.Exists(target) && new FileInfo(target).Length == new FileInfo(source).Length)
            {
                return true; // already copied (e.g. before a restart)
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var part = target + PartSuffix;
            var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, useAsync: true);
            await using (input.ConfigureAwait(false))
            {
                var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
                await using (output.ConfigureAwait(false))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(part, target, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not copy a finished file of download {Id} to the library", record.Id);
            return false;
        }
    }
}
