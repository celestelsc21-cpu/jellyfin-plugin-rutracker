using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.RuTracker.Tracker;

/// <summary>
/// RuTracker site client.
/// </summary>
public interface IRuTrackerClient
{
    /// <summary>
    /// Searches torrents. Results are cached for a short time.
    /// </summary>
    /// <param name="query">Normalized query (see <see cref="SearchQuery.Normalize"/>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Found torrents in site order (most seeded first).</returns>
    /// <exception cref="RuTrackerException">The site is unreachable or login failed.</exception>
    Task<IReadOnlyList<TorrentInfo>> SearchAsync(string query, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies that the configured credentials allow logging in.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when the check succeeds.</returns>
    /// <exception cref="RuTrackerException">Login failed; the message explains why.</exception>
    Task CheckLoginAsync(CancellationToken cancellationToken);
}
