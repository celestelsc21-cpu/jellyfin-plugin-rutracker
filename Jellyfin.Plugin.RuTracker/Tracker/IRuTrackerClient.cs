using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RuTracker.Diagnostics;

namespace Jellyfin.Plugin.RuTracker.Tracker;

/// <summary>
/// RuTracker site client.
/// </summary>
public interface IRuTrackerClient
{
    /// <summary>
    /// Searches torrents. Results are cached for a short time.
    /// Falls back to the alternative address when the main one is unreachable.
    /// </summary>
    /// <param name="query">Normalized query (see <see cref="SearchQuery.Normalize"/>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Found torrents in site order (most seeded first).</returns>
    /// <exception cref="RuTrackerException">The site is unreachable or login failed.</exception>
    Task<IReadOnlyList<TorrentInfo>> SearchAsync(string query, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the .torrent file of a topic (or at least its magnet link) for handing it to qBittorrent.
    /// Results are cached for a short time.
    /// </summary>
    /// <param name="topicId">Topic id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The topic torrent.</returns>
    /// <exception cref="RuTrackerException">Neither the file nor a magnet link could be obtained.</exception>
    Task<TopicTorrent> GetTopicTorrentAsync(long topicId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks every configured address step by step: reachability, login, session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report.</returns>
    Task<DiagnosticReport> DiagnoseAsync(CancellationToken cancellationToken);
}
