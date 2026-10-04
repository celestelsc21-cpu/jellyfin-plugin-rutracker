using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RuTracker.QBittorrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RuTracker.Downloads;

/// <summary>
/// Background loop that keeps episode priorities in watching order. Polls often
/// while downloads are active and rarely otherwise; failures never stop the loop.
/// </summary>
internal sealed class DownloadMonitor : BackgroundService
{
    private static readonly TimeSpan ActiveInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ErrorInterval = TimeSpan.FromSeconds(30);

    private readonly IDownloadManager _manager;
    private readonly ILogger<DownloadMonitor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadMonitor"/> class.
    /// </summary>
    /// <param name="manager">Download manager.</param>
    /// <param name="logger">Logger.</param>
    public DownloadMonitor(IDownloadManager manager, ILogger<DownloadMonitor> logger)
    {
        _manager = manager;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Do not compete with server startup.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                delay = await _manager.ProcessAsync(stoppingToken).ConfigureAwait(false) ? ActiveInterval : IdleInterval;
            }
            catch (QBittorrentException ex)
            {
                _logger.LogWarning("Download monitor: {Reason}", ex.Message);
                delay = ErrorInterval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The loop must survive any unexpected error.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogError(ex, "Download monitor pass failed");
                delay = ErrorInterval;
            }

            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }
}
