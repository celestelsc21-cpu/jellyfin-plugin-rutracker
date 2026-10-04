using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RuTracker.Api.Models;
using Jellyfin.Plugin.RuTracker.Configuration;
using Jellyfin.Plugin.RuTracker.Library;
using Jellyfin.Plugin.RuTracker.QBittorrent;
using Jellyfin.Plugin.RuTracker.Streaming;
using Jellyfin.Plugin.RuTracker.Torrents;
using Jellyfin.Plugin.RuTracker.Tracker;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RuTracker.Downloads;

/// <summary>
/// Starts downloads, keeps qBittorrent file priorities in watching order and reports progress.
/// </summary>
internal sealed class DownloadManager : IDownloadManager, IDisposable
{
    private readonly IRuTrackerClient _rutracker;
    private readonly IQBittorrentClient _qbittorrent;
    private readonly DownloadStore _store;
    private readonly IPluginConfigurationAccessor _config;
    private readonly LibraryPublisher _publisher;
    private readonly ILogger<DownloadManager> _logger;
    private readonly SemaphoreSlim _processGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, DownloadSnapshot> _snapshots = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadManager"/> class.
    /// </summary>
    /// <param name="rutracker">RuTracker client.</param>
    /// <param name="qbittorrent">qBittorrent client.</param>
    /// <param name="store">Download store.</param>
    /// <param name="config">Configuration accessor.</param>
    /// <param name="publisher">Library publisher.</param>
    /// <param name="logger">Logger.</param>
    public DownloadManager(
        IRuTrackerClient rutracker,
        IQBittorrentClient qbittorrent,
        DownloadStore store,
        IPluginConfigurationAccessor config,
        LibraryPublisher publisher,
        ILogger<DownloadManager> logger)
    {
        _rutracker = rutracker;
        _qbittorrent = qbittorrent;
        _store = store;
        _config = config;
        _publisher = publisher;
        _logger = logger;
    }

    /// <summary>
    /// Gets the configured destinations.
    /// </summary>
    /// <returns>Targets without paths.</returns>
    public IReadOnlyList<DownloadTargetDto> GetTargets()
        => (_config.Current.DownloadTargets ?? [])
            .Select(t => new DownloadTargetDto(t.Id, t.Name, t.Kind, t.IsDefault))
            .ToList();

    /// <summary>
    /// Gets the video files of a topic, in watching order, to choose where to start.
    /// </summary>
    /// <param name="topicId">Topic id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Files (empty when only a magnet link is available).</returns>
    public async Task<TopicFilesDto> GetTopicFilesAsync(long topicId, CancellationToken cancellationToken)
    {
        var torrent = await _rutracker.GetTopicTorrentAsync(topicId, cancellationToken).ConfigureAwait(false);
        var files = torrent.Meta is null
            ? []
            : EpisodePlanner.VideosInOrder(torrent.Meta.Files)
                .Select(f => new TopicFileDto(f.Index, f.Path, EpisodePlanner.Label(f.Path), f.Length, true))
                .ToList();
        return new TopicFilesDto(
            topicId,
            torrent.Title,
            files,
            ReleaseTitle.FolderName(torrent.Title, "RuTracker " + topicId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            torrent.Meta is not null);
    }

    /// <summary>
    /// Starts a download.
    /// </summary>
    /// <param name="userId">Requesting user.</param>
    /// <param name="request">Request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result with a user-facing message.</returns>
    /// <exception cref="DownloadException">The request cannot be fulfilled.</exception>
    public async Task<DownloadCreatedDto> CreateAsync(Guid userId, CreateDownloadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var config = _config.Current;
        var target = ResolveTarget(config, request.Kind, request.TargetId);
        var copyToLibrary = config.Placement == PlacementMode.CopyAfterComplete;
        if (copyToLibrary && !PathRules.IsAbsoluteLinuxPath(config.StagingJellyfinPath))
        {
            throw new DownloadException("Включён режим копирования, но промежуточная папка не задана. Попросите администратора проверить настройки.");
        }

        var existing = (await _store.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(r => r.TopicId == request.TopicId && r.State is DownloadState.Queued or DownloadState.Downloading);
        if (existing is not null)
        {
            return new DownloadCreatedDto(existing.Id, DownloadMessages.AlreadyDownloading(existing.Kind, existing.Title), true);
        }

        TopicTorrent torrent;
        try
        {
            torrent = await _rutracker.GetTopicTorrentAsync(request.TopicId, cancellationToken).ConfigureAwait(false);
        }
        catch (RuTrackerException ex)
        {
            throw new DownloadException(ex.Message, ex);
        }

        var hash = torrent.InfoHash ?? throw new DownloadException("Не удалось определить раздачу: нет ни торрент-файла, ни magnet-ссылки.");

        // "Title (Year)" folder so Jellyfin recognises the movie or series.
        var id = request.TopicId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var folderName = ReleaseTitle.FolderName(torrent.Title, "RuTracker " + id);
        var libraryFolder = target.JellyfinPath.TrimEnd('/') + "/" + folderName;
        var downloadFolder = copyToLibrary
            ? config.StagingJellyfinPath.TrimEnd('/') + "/" + folderName + " [" + id + "]"
            : libraryFolder;
        var mapper = new PathMapper(config.PathMappings ?? []);
        if (!mapper.TryToQBittorrent(downloadFolder, out var qbFolder))
        {
            throw new DownloadException("Папка загрузки не покрыта соответствием путей. Проверьте настройки плагина.");
        }

        if (!copyToLibrary && torrent.Meta is not null)
        {
            // Hide every file from the library scanner before qBittorrent creates them.
            _publisher.HideBeforeStart(downloadFolder, torrent.Meta.Files);
        }
        if (request.StartFileIndex is { } start && torrent.Meta is not null && torrent.Meta.Files.All(f => f.Index != start))
        {
            throw new DownloadException("Выбранной серии нет в раздаче.");
        }

        try
        {
            // With a .torrent file the file list is known: add stopped, set priorities, then start.
            // A magnet link needs metadata first, so it starts right away.
            await _qbittorrent.AddTorrentAsync(torrent, qbFolder, config.QBittorrentCategory, stopped: torrent.Meta is not null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (QBittorrentException ex)
        {
            throw new DownloadException(ex.Message, ex);
        }

        var record = new DownloadRecord
        {
            TopicId = request.TopicId,
            Title = torrent.Title,
            Kind = request.Kind,
            TargetId = target.Id,
            JellyfinFolder = downloadFolder,
            LibraryFolder = libraryFolder,
            CopyToLibrary = copyToLibrary,
            QBittorrentFolder = qbFolder,
            InfoHash = hash,
            UserId = userId,
            StartFileIndex = request.StartFileIndex ?? (torrent.Meta is null ? null : EpisodePlanner.FileIndexOfEpisode(torrent.Meta.Files, request.StartEpisodeNumber)),
            StartEpisodeNumber = request.StartEpisodeNumber,
            Order = torrent.Meta is null
                ? []
                : [.. EpisodePlanner.BuildOrder(torrent.Meta.Files, request.StartFileIndex ?? EpisodePlanner.FileIndexOfEpisode(torrent.Meta.Files, request.StartEpisodeNumber))],
            Started = torrent.Meta is null
        };

        await _store.UpdateAsync(
            list =>
            {
                list.Add(record);
                return true;
            },
            save: true,
            cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Download {Id} started for topic {TopicId} by user {UserId}", record.Id, record.TopicId, userId);

        // Apply priorities and start immediately instead of waiting for the next monitor pass.
        await ProcessSafelyAsync(cancellationToken).ConfigureAwait(false);
        return new DownloadCreatedDto(record.Id, DownloadMessages.WillBeDownloaded(record.Kind, record.Title), false);
    }

    /// <summary>
    /// Lists downloads with the latest known progress.
    /// </summary>
    /// <param name="callerId">Caller user id.</param>
    /// <param name="isAdministrator">Caller is an administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Downloads, newest first.</returns>
    public async Task<IReadOnlyList<DownloadDto>> ListAsync(Guid callerId, bool isAdministrator, CancellationToken cancellationToken)
    {
        var records = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var targets = (_config.Current.DownloadTargets ?? []).ToDictionary(t => t.Id, t => t.Name);
        return records.Select(r =>
        {
            _snapshots.TryGetValue(r.Id, out var s);
            return new DownloadDto(
                r.Id,
                r.TopicId,
                r.Title,
                r.Kind,
                targets.GetValueOrDefault(r.TargetId, r.JellyfinFolder),
                r.State,
                r.State == DownloadState.Completed ? 1 : s?.Progress ?? 0,
                s?.Speed ?? 0,
                s?.Eta,
                s?.CurrentLabel,
                s?.CurrentProgress,
                s?.VideoCount ?? r.Order.Count,
                s?.VideosDone ?? 0,
                r.Created,
                isAdministrator || r.UserId == callerId);
        }).ToList();
    }

    /// <summary>
    /// Gets the video files of a download with their progress.
    /// </summary>
    /// <param name="id">Download id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Files in watching order.</returns>
    public async Task<IReadOnlyList<DownloadFileDto>> GetFilesAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await FindAsync(id, cancellationToken).ConfigureAwait(false);
        var files = await GetQbFilesAsync(record.InfoHash, cancellationToken).ConfigureAwait(false);
        var byIndex = files.ToDictionary(f => f.Index);
        var current = record.Order.FirstOrDefault(i => byIndex.TryGetValue(i, out var f) && f.Progress < 1, -1);
        return EpisodePlanner.VideosInOrder(files.Select(f => new TorrentFileEntry(f.Index, f.Name, f.Size)))
            .Select(f => new DownloadFileDto(f.Index, EpisodePlanner.Label(f.Path), byIndex[f.Index].Progress, f.Index == current))
            .ToList();
    }

    /// <summary>
    /// Changes the episode to watch first; the rest keeps downloading after it.
    /// </summary>
    /// <param name="id">Download id.</param>
    /// <param name="fileIndex">File index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task SetStartAsync(Guid id, int fileIndex, CancellationToken cancellationToken)
    {
        var record = await FindAsync(id, cancellationToken).ConfigureAwait(false);
        var files = await GetQbFilesAsync(record.InfoHash, cancellationToken).ConfigureAwait(false);
        var entries = files.Select(f => new TorrentFileEntry(f.Index, f.Name, f.Size)).ToList();
        if (entries.Count > 0 && entries.All(f => f.Index != fileIndex || !EpisodePlanner.IsVideo(f.Path)))
        {
            throw new DownloadException("Такой серии нет в раздаче.");
        }

        await _store.UpdateAsync(
            list =>
            {
                var live = list.First(r => r.Id == id);
                live.StartFileIndex = fileIndex;
                live.Order = entries.Count > 0 ? [.. EpisodePlanner.BuildOrder(entries, fileIndex)] : live.Order;
                return true;
            },
            save: true,
            cancellationToken).ConfigureAwait(false);

        await ProcessSafelyAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Cancels a download: removes it from qBittorrent and from the list.
    /// </summary>
    /// <param name="id">Download id.</param>
    /// <param name="deleteFiles">Also delete downloaded files.</param>
    /// <param name="callerId">Caller user id.</param>
    /// <param name="isAdministrator">Caller is an administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task DeleteAsync(Guid id, bool deleteFiles, Guid callerId, bool isAdministrator, CancellationToken cancellationToken)
    {
        var record = await FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (!isAdministrator && record.UserId != callerId)
        {
            throw new UnauthorizedAccessException("Удалить загрузку может только тот, кто её начал, или администратор.");
        }

        try
        {
            await _qbittorrent.DeleteAsync(record.InfoHash, deleteFiles, cancellationToken).ConfigureAwait(false);
        }
        catch (QBittorrentException ex)
        {
            throw new DownloadException(ex.Message, ex);
        }

        await _store.UpdateAsync(list => list.RemoveAll(r => r.Id == id), save: true, cancellationToken).ConfigureAwait(false);
        _snapshots.TryRemove(id, out _);
        _logger.LogInformation("Download {Id} removed by user {UserId} (files deleted: {DeleteFiles})", id, callerId, deleteFiles);
    }

    /// <summary>
    /// Synchronises every active download with qBittorrent: applies priorities in
    /// watching order, starts torrents and refreshes progress.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when there are active downloads.</returns>
    public async Task<bool> ProcessAsync(CancellationToken cancellationToken)
    {
        await _processGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var active = (await _store.GetAllAsync(cancellationToken).ConfigureAwait(false))
                .Where(r => r.State is DownloadState.Queued or DownloadState.Downloading)
                .ToList();
            if (active.Count == 0)
            {
                return false;
            }

            var torrents = (await _qbittorrent.GetTorrentsAsync(active.Select(r => r.InfoHash).ToList(), cancellationToken).ConfigureAwait(false))
                .ToDictionary(t => t.Hash, StringComparer.OrdinalIgnoreCase);

            var updates = new List<DownloadRecord>();
            foreach (var record in active)
            {
                var updated = await ProcessOneAsync(record, torrents.GetValueOrDefault(record.InfoHash), cancellationToken).ConfigureAwait(false);
                if (updated)
                {
                    updates.Add(record);
                }
            }

            if (updates.Count > 0)
            {
                await _store.UpdateAsync(
                    list =>
                    {
                        foreach (var changed in updates)
                        {
                            var live = list.FirstOrDefault(r => r.Id == changed.Id);
                            if (live is not null)
                            {
                                live.Order = changed.Order;
                                live.StartFileIndex = changed.StartFileIndex;
                                live.Started = changed.Started;
                                live.State = changed.State;
                                live.Published = changed.Published;
                            }
                        }

                        return true;
                    },
                    save: true,
                    cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        finally
        {
            _processGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChannelDownload>> GetChannelDownloadsAsync(CancellationToken cancellationToken)
        => (await _store.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(r => r.State is DownloadState.Queued or DownloadState.Downloading)
            .Select(r => new ChannelDownload(r.Id, r.Title, r.Kind, r.Created))
            .ToList();

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChannelFile>> GetChannelFilesAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = (await _store.GetAllAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(r => r.Id == id);
        if (record is null)
        {
            return [];
        }

        var files = await _qbittorrent.GetFilesAsync(record.InfoHash, cancellationToken).ConfigureAwait(false);
        var byIndex = files.ToDictionary(f => f.Index);
        return EpisodePlanner.VideosInOrder(files.Select(f => new TorrentFileEntry(f.Index, f.Name, f.Size)))
            .Select((f, i) => new ChannelFile(f.Index, i + 1, f.Path, EpisodePlanner.Label(f.Path), f.Length, byIndex[f.Index].Progress))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<StreamSource?> GetStreamSourceAsync(Guid id, int fileIndex, CancellationToken cancellationToken)
    {
        var record = (await _store.GetAllAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(r => r.Id == id);
        if (record is null)
        {
            return null;
        }

        var file = (await _qbittorrent.GetFilesAsync(record.InfoHash, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(f => f.Index == fileIndex);
        if (file is null || file.PieceRange.Count < 2 || !EpisodePlanner.IsVideo(file.Name))
        {
            return null;
        }

        var path = SafePath.Combine(record.JellyfinFolder, file.Name);
        if (path is null)
        {
            return null;
        }

        // qBittorrent may keep unfinished files under a ".!qB" suffix.
        if (!System.IO.File.Exists(path) && System.IO.File.Exists(path + ".!qB"))
        {
            path += ".!qB";
        }

        return System.IO.File.Exists(path)
            ? new StreamSource(path, file.Size, record.InfoHash, file.PieceRange[0], file.PieceRange[1], file.Progress >= 1)
            : null;
    }

    /// <inheritdoc />
    public async Task EnsureWatchingAsync(Guid id, int fileIndex, CancellationToken cancellationToken)
    {
        var record = (await _store.GetAllAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(r => r.Id == id);
        if (record is null || record.State is not (DownloadState.Queued or DownloadState.Downloading))
        {
            return;
        }

        var files = await _qbittorrent.GetFilesAsync(record.InfoHash, cancellationToken).ConfigureAwait(false);
        var byIndex = files.ToDictionary(f => f.Index);
        if (!byIndex.TryGetValue(fileIndex, out var watched) || watched.Progress >= 1)
        {
            return;
        }

        var current = record.Order.FirstOrDefault(i => byIndex.TryGetValue(i, out var f) && f.Progress < 1, -1);
        if (current != fileIndex)
        {
            _logger.LogInformation("Playback started on a later episode of download {Id}; moving it to the front", id);
            await SetStartAsync(id, fileIndex, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _processGate.Dispose();

    private static DownloadTarget ResolveTarget(PluginConfiguration config, MediaKind kind, Guid? targetId)
    {
        var targets = config.DownloadTargets ?? [];
        if (targetId is { } id)
        {
            return targets.FirstOrDefault(t => t.Id == id)
                ?? throw new DownloadException("Выбранная папка загрузки не найдена в настройках.");
        }

        return targets.Where(t => t.Kind == kind).OrderByDescending(t => t.IsDefault).FirstOrDefault()
            ?? throw new DownloadException("Для типа «" + ConfigurationValidator.KindName(kind) + "» не настроена папка загрузки. Попросите администратора добавить её.");
    }

    private async Task<bool> ProcessOneAsync(DownloadRecord record, QbTorrent? torrent, CancellationToken cancellationToken)
    {
        if (torrent is null)
        {
            // Give qBittorrent a moment after adding; afterwards a missing torrent was removed outside the plugin.
            if (DateTimeOffset.UtcNow - record.Created > TimeSpan.FromMinutes(2))
            {
                record.State = DownloadState.Removed;
                _snapshots.TryRemove(record.Id, out _);
                _logger.LogInformation("Download {Id} is no longer in qBittorrent", record.Id);
                return true;
            }

            return false;
        }

        var files = await _qbittorrent.GetFilesAsync(record.InfoHash, cancellationToken).ConfigureAwait(false);
        if (files.Count == 0)
        {
            _snapshots[record.Id] = new DownloadSnapshot(torrent.Progress, torrent.DownloadSpeed, Eta(torrent), "Получение списка файлов…", null, 0, 0);
            return false; // magnet link still fetching metadata
        }

        var changed = false;
        var entries = files.Select(f => new TorrentFileEntry(f.Index, f.Name, f.Size)).ToList();
        if (record.Order.Count == 0)
        {
            record.StartFileIndex ??= EpisodePlanner.FileIndexOfEpisode(entries, record.StartEpisodeNumber);
            record.Order = [.. EpisodePlanner.BuildOrder(entries, record.StartFileIndex)];
            changed = true;
        }

        var byIndex = files.ToDictionary(f => f.Index);
        bool IsComplete(int index) => byIndex.TryGetValue(index, out var f) && f.Progress >= 1;

        var desired = EpisodePlanner.Priorities(entries, record.Order, IsComplete);
        foreach (var group in files.Where(f => desired.TryGetValue(f.Index, out var p) && p != f.Priority).GroupBy(f => desired[f.Index]))
        {
            await _qbittorrent.SetFilePriorityAsync(record.InfoHash, group.Select(f => f.Index).ToList(), group.Key, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!record.Started)
        {
            await _qbittorrent.StartAsync(record.InfoHash, cancellationToken).ConfigureAwait(false);
            record.Started = true;
            changed = true;
        }

        if (await _publisher.PublishAsync(record, files, cancellationToken).ConfigureAwait(false))
        {
            changed = true;
        }

        var state = torrent.Progress >= 1 ? DownloadState.Completed : DownloadState.Downloading;
        if (state != record.State)
        {
            record.State = state;
            changed = true;
            if (state == DownloadState.Completed)
            {
                _logger.LogInformation("Download {Id} completed", record.Id);
            }
        }

        var currentIndex = record.Order.FirstOrDefault(i => !IsComplete(i), -1);
        _snapshots[record.Id] = new DownloadSnapshot(
            torrent.Progress,
            torrent.DownloadSpeed,
            Eta(torrent),
            currentIndex >= 0 && byIndex.TryGetValue(currentIndex, out var cf) ? EpisodePlanner.Label(cf.Name) : null,
            currentIndex >= 0 && byIndex.TryGetValue(currentIndex, out var cp) ? cp.Progress : null,
            record.Order.Count,
            record.Order.Count(IsComplete));
        return changed;
    }

    private static long? Eta(QbTorrent torrent) => torrent.Eta is > 0 and < 8640000 ? torrent.Eta : null;

    private async Task ProcessSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ProcessAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (QBittorrentException ex)
        {
            _logger.LogWarning("Could not update download priorities right away: {Reason}", ex.Message);
        }
    }

    private async Task<DownloadRecord> FindAsync(Guid id, CancellationToken cancellationToken)
        => (await _store.GetAllAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(r => r.Id == id)
            ?? throw new KeyNotFoundException("Загрузка не найдена.");

    private async Task<IReadOnlyList<QbFile>> GetQbFilesAsync(string hash, CancellationToken cancellationToken)
    {
        try
        {
            return await _qbittorrent.GetFilesAsync(hash, cancellationToken).ConfigureAwait(false);
        }
        catch (QBittorrentException ex)
        {
            throw new DownloadException(ex.Message, ex);
        }
    }

    private sealed record DownloadSnapshot(
        double Progress,
        long Speed,
        long? Eta,
        string? CurrentLabel,
        double? CurrentProgress,
        int VideoCount,
        int VideosDone);
}
