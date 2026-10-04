using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RuTracker.Streaming;

/// <summary>
/// Probes a stream with ffprobe so Jellyfin knows its codecs.
/// </summary>
/// <remarks>
/// Without stream information Jellyfin cannot tell whether the client plays the codecs and
/// re-encodes everything, which is slower than real time on a weak CPU. With it, compatible
/// files are only remuxed. Probing goes through the stream endpoint, so it waits for the
/// first pieces instead of reading unwritten parts of the file. Results are cached.
/// </remarks>
internal sealed class StreamProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan CacheTime = TimeSpan.FromHours(12);

    private readonly IMediaEncoder _encoder;
    private readonly IMemoryCache _cache;
    private readonly ILogger<StreamProbe> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamProbe"/> class.
    /// </summary>
    /// <param name="encoder">Media encoder (ffprobe).</param>
    /// <param name="cache">Memory cache.</param>
    /// <param name="logger">Logger.</param>
    public StreamProbe(IMediaEncoder encoder, IMemoryCache cache, ILogger<StreamProbe> logger)
    {
        _encoder = encoder;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Fills codec, duration and bitrate information of <paramref name="source"/>.
    /// Leaves it unchanged when probing fails (Jellyfin then transcodes, as before).
    /// </summary>
    /// <param name="source">Media source whose <see cref="MediaSourceInfo.Path"/> is the stream URL.</param>
    /// <param name="cacheKey">Stable key of the file (download id and file index).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task ApplyAsync(MediaSourceInfo source, string cacheKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var key = "rutracker:probe:" + cacheKey;
        if (!_cache.TryGetValue(key, out MediaSourceInfo? info) || info is null)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);
            try
            {
                var probeSource = new MediaSourceInfo
                {
                    Id = source.Id,
                    Path = source.Path,
                    Protocol = source.Protocol,
                    IsRemote = true,
                    Container = source.Container,
                    AnalyzeDurationMs = 3000
                };
                info = await _encoder.GetMediaInfo(
                    new MediaInfoRequest { MediaSource = probeSource, MediaType = DlnaProfileType.Video, ExtractChapters = false },
                    timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Probing a downloading file timed out; Jellyfin will transcode it");
                return;
            }
#pragma warning disable CA1031 // A failed probe must never break playback.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                _logger.LogInformation("Probing a downloading file failed ({Reason}); Jellyfin will transcode it", ex.Message);
                return;
            }

            if (info is null || info.MediaStreams is null || !info.MediaStreams.Any(s => s.Type == MediaStreamType.Video))
            {
                return;
            }

            _cache.Set(key, info, CacheTime);
        }

        source.MediaStreams = info.MediaStreams;
        source.Container = string.IsNullOrEmpty(info.Container) ? source.Container : info.Container;
        source.Bitrate = info.Bitrate;
        source.RunTimeTicks = info.RunTimeTicks;
        source.Formats = info.Formats;
        source.Timestamp = info.Timestamp;
        source.VideoType = info.VideoType;
        source.Video3DFormat = info.Video3DFormat;
        source.DefaultAudioStreamIndex = info.MediaStreams.FirstOrDefault(s => s.Type == MediaStreamType.Audio)?.Index;
        source.AnalyzeDurationMs = 3000;
    }
}
