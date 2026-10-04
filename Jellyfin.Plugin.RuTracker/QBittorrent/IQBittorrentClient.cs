using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RuTracker.Diagnostics;
using Jellyfin.Plugin.RuTracker.Tracker;

namespace Jellyfin.Plugin.RuTracker.QBittorrent;

/// <summary>
/// qBittorrent Web API client.
/// </summary>
public interface IQBittorrentClient
{
    /// <summary>
    /// Checks the connection step by step: Web UI reachability, login, version, default folder.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report.</returns>
    Task<DiagnosticReport> DiagnoseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds a torrent (file or magnet) with sequential download enabled.
    /// Succeeds when the torrent is already present.
    /// </summary>
    /// <param name="torrent">Torrent to add.</param>
    /// <param name="savePath">Folder as qBittorrent sees it.</param>
    /// <param name="category">Category marking plugin-owned torrents.</param>
    /// <param name="stopped">Add without starting (to set file priorities first).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    /// <exception cref="QBittorrentException">qBittorrent rejected the torrent or is unreachable.</exception>
    Task AddTorrentAsync(TopicTorrent torrent, string savePath, string category, bool stopped, CancellationToken cancellationToken);

    /// <summary>
    /// Gets torrent states.
    /// </summary>
    /// <param name="hashes">Info hashes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Torrents qBittorrent knows about; missing hashes are absent.</returns>
    Task<IReadOnlyList<QbTorrent>> GetTorrentsAsync(IReadOnlyCollection<string> hashes, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the files of a torrent; empty while a magnet link is still fetching metadata.
    /// </summary>
    /// <param name="hash">Info hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Files.</returns>
    Task<IReadOnlyList<QbFile>> GetFilesAsync(string hash, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the priority of several files.
    /// </summary>
    /// <param name="hash">Info hash.</param>
    /// <param name="fileIndexes">File indexes.</param>
    /// <param name="priority">qBittorrent priority.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task SetFilePriorityAsync(string hash, IReadOnlyCollection<int> fileIndexes, int priority, CancellationToken cancellationToken);

    /// <summary>
    /// Starts (resumes) a torrent.
    /// </summary>
    /// <param name="hash">Info hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task StartAsync(string hash, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a torrent.
    /// </summary>
    /// <param name="hash">Info hash.</param>
    /// <param name="deleteFiles">Also delete downloaded files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task DeleteAsync(string hash, bool deleteFiles, CancellationToken cancellationToken);
}
