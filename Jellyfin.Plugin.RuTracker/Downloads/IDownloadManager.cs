using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RuTracker.Api.Models;
using Jellyfin.Plugin.RuTracker.Streaming;

namespace Jellyfin.Plugin.RuTracker.Downloads;

/// <summary>
/// Starts downloads, keeps qBittorrent file priorities in watching order and reports progress.
/// </summary>
public interface IDownloadManager
{
    /// <summary>
    /// Gets the configured destinations.
    /// </summary>
    /// <returns>Targets without paths.</returns>
    IReadOnlyList<DownloadTargetDto> GetTargets();

    /// <summary>
    /// Gets the video files of a topic, in watching order.
    /// </summary>
    /// <param name="topicId">Topic id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Files.</returns>
    Task<TopicFilesDto> GetTopicFilesAsync(long topicId, CancellationToken cancellationToken);

    /// <summary>
    /// Starts a download.
    /// </summary>
    /// <param name="userId">Requesting user.</param>
    /// <param name="request">Request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result with a user-facing message.</returns>
    Task<DownloadCreatedDto> CreateAsync(Guid userId, CreateDownloadRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Lists downloads with the latest known progress.
    /// </summary>
    /// <param name="callerId">Caller user id.</param>
    /// <param name="isAdministrator">Caller is an administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Downloads, newest first.</returns>
    Task<IReadOnlyList<DownloadDto>> ListAsync(Guid callerId, bool isAdministrator, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the video files of a download with their progress.
    /// </summary>
    /// <param name="id">Download id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Files in watching order.</returns>
    Task<IReadOnlyList<DownloadFileDto>> GetFilesAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the episode to watch first.
    /// </summary>
    /// <param name="id">Download id.</param>
    /// <param name="fileIndex">File index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task SetStartAsync(Guid id, int fileIndex, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels a download.
    /// </summary>
    /// <param name="id">Download id.</param>
    /// <param name="deleteFiles">Also delete downloaded files.</param>
    /// <param name="callerId">Caller user id.</param>
    /// <param name="isAdministrator">Caller is an administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task DeleteAsync(Guid id, bool deleteFiles, Guid callerId, bool isAdministrator, CancellationToken cancellationToken);

    /// <summary>
    /// Synchronises every active download with qBittorrent.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when there are active downloads.</returns>
    Task<bool> ProcessAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the downloads currently in progress, for the channel.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Active downloads, newest first.</returns>
    Task<IReadOnlyList<ChannelDownload>> GetChannelDownloadsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the video files of a download in natural order, for the channel.
    /// </summary>
    /// <param name="id">Download id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Files; empty when the download is unknown or metadata is not ready.</returns>
    Task<IReadOnlyList<ChannelFile>> GetChannelFilesAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Locates a file of a download for streaming.
    /// </summary>
    /// <param name="id">Download id.</param>
    /// <param name="fileIndex">File index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The source, or <c>null</c> when unknown or not created on disk yet.</returns>
    Task<StreamSource?> GetStreamSourceAsync(Guid id, int fileIndex, CancellationToken cancellationToken);

    /// <summary>
    /// Makes sure the file being watched downloads first (no-op when it already does or is finished).
    /// </summary>
    /// <param name="id">Download id.</param>
    /// <param name="fileIndex">File index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task EnsureWatchingAsync(Guid id, int fileIndex, CancellationToken cancellationToken);
}
