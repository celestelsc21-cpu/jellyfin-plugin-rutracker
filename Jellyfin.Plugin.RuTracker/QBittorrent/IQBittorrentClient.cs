using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RuTracker.Diagnostics;

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
}
