using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RuTracker.Downloads;
using Jellyfin.Plugin.RuTracker.QBittorrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RuTracker.Streaming;

/// <summary>
/// Serves a file that may still be downloading, with HTTP range support: bytes are sent as
/// soon as the torrent pieces holding them are downloaded.
/// </summary>
/// <remarks>
/// Anonymous on purpose: the only caller is Jellyfin itself (its stream proxy and ffmpeg),
/// which cannot attach a user token. Access is limited to loopback connections and to
/// URLs signed by the plugin (<see cref="StreamTokens"/>), which only the channel hands out
/// to users who passed the role check.
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("RuTracker/Stream")]
[ApiExplorerSettings(IgnoreApi = true)]
public class StreamController : ControllerBase
{
    private const int ChunkSize = 1 << 20;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(90);
    private static readonly FileExtensionContentTypeProvider ContentTypes = CreateContentTypes();

    private readonly IDownloadManager _manager;
    private readonly IQBittorrentClient _qbittorrent;
    private readonly StreamTokens _tokens;
    private readonly IMemoryCache _cache;
    private readonly ILogger<StreamController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamController"/> class.
    /// </summary>
    /// <param name="manager">Download manager.</param>
    /// <param name="qbittorrent">qBittorrent client.</param>
    /// <param name="tokens">Stream URL signer.</param>
    /// <param name="cache">Memory cache.</param>
    /// <param name="logger">Logger.</param>
    public StreamController(
        IDownloadManager manager,
        IQBittorrentClient qbittorrent,
        StreamTokens tokens,
        IMemoryCache cache,
        ILogger<StreamController> logger)
    {
        _manager = manager;
        _qbittorrent = qbittorrent;
        _tokens = tokens;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Streams a file of a download.
    /// </summary>
    /// <param name="id">Download id.</param>
    /// <param name="fileIndex">File index inside the torrent.</param>
    /// <param name="exp">Link expiry (Unix seconds).</param>
    /// <param name="sig">Link signature.</param>
    /// <returns>A task.</returns>
    [HttpGet("{id}/{fileIndex}")]
    [HttpHead("{id}/{fileIndex}")]
    public async Task Get([FromRoute] Guid id, [FromRoute] int fileIndex, [FromQuery] long exp, [FromQuery] string? sig)
    {
        var cancellationToken = HttpContext.RequestAborted;
        if (!IsLoopback(HttpContext.Connection.RemoteIpAddress) || !_tokens.IsValid(id, fileIndex, exp, sig))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        StreamSource? source;
        try
        {
            source = await _manager.GetStreamSourceAsync(id, fileIndex, cancellationToken).ConfigureAwait(false);
            await _manager.EnsureWatchingAsync(id, fileIndex, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is QBittorrentException or DownloadException or KeyNotFoundException)
        {
            _logger.LogWarning("Stream {Id}/{File} unavailable: {Reason}", id, fileIndex, ex.Message);
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (source is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var hasRange = !string.IsNullOrEmpty(Request.Headers.Range);
        if (!ByteRange.TryParse(Request.Headers.Range, source.Size, out var start, out var end))
        {
            Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
            Response.Headers.ContentRange = "bytes */" + source.Size.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return;
        }

        Response.StatusCode = hasRange ? StatusCodes.Status206PartialContent : StatusCodes.Status200OK;
        Response.Headers.AcceptRanges = "bytes";
        Response.ContentType = ContentTypes.TryGetContentType(source.Path.Replace(".!qB", string.Empty, StringComparison.Ordinal), out var type) ? type : "application/octet-stream";
        Response.ContentLength = end - start + 1;
        if (hasRange)
        {
            Response.Headers.ContentRange = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"bytes {start}-{end}/{source.Size}");
        }

        if (HttpMethods.IsHead(Request.Method))
        {
            return;
        }

        try
        {
            await CopyAsync(source, start, end - start + 1, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The player stopped or seeked: normal.
        }
        catch (Exception ex) when (ex is IOException or QBittorrentException)
        {
            _logger.LogWarning("Stream {Id}/{File} interrupted: {Reason}", id, fileIndex, ex.Message);
            HttpContext.Abort(); // Content-Length already promised: let the client retry with a range
        }
    }

    private static bool IsLoopback(IPAddress? address)
        => address is not null
           && (IPAddress.IsLoopback(address) || (address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4())));

    private static FileExtensionContentTypeProvider CreateContentTypes()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".mkv"] = "video/x-matroska";
        provider.Mappings[".m2ts"] = "video/mp2t";
        provider.Mappings[".mts"] = "video/mp2t";
        provider.Mappings[".ts"] = "video/mp2t";
        return provider;
    }

    private async Task CopyAsync(StreamSource source, long position, long remaining, CancellationToken cancellationToken)
    {
        var pieceSize = source.Complete ? 0 : await GetPieceSizeAsync(source.InfoHash, cancellationToken).ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            // No lock is taken: qBittorrent keeps writing (and may rename) the file meanwhile.
            using (var file = UnlockedFileReader.Open(source.Path))
            {
                var waited = TimeSpan.Zero;
                var complete = source.Complete || pieceSize <= 0;
                while (remaining > 0)
                {
                    long readable;
                    if (complete)
                    {
                        readable = remaining;
                    }
                    else
                    {
                        var states = await GetPieceStatesAsync(source.InfoHash, cancellationToken).ConfigureAwait(false);
                        readable = PieceMath.ReadableFrom(states, source.FirstPiece, source.LastPiece, pieceSize, position, source.Size);
                        complete = readable >= source.Size - position;
                    }

                    var toRead = (int)Math.Min(Math.Min(readable, remaining), ChunkSize);
                    var read = 0;
                    if (toRead > 0)
                    {
                        read = file.Read(buffer, toRead, position);
                    }

                    if (read <= 0)
                    {
                        // Not downloaded yet: wait for the pieces (they come first thanks to the priorities).
                        if (waited >= MaxWait)
                        {
                            _logger.LogInformation("Stream waited too long for data at byte {Position}; closing", position);
                            HttpContext.Abort();
                            return;
                        }

                        await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                        waited += PollInterval;
                        continue;
                    }

                    waited = TimeSpan.Zero;
                    await Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    position += read;
                    remaining -= read;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<long> GetPieceSizeAsync(string hash, CancellationToken cancellationToken)
    {
        var key = "rutracker:piecesize:" + hash;
        if (_cache.TryGetValue(key, out long size))
        {
            return size;
        }

        size = await _qbittorrent.GetPieceSizeAsync(hash, cancellationToken).ConfigureAwait(false);
        if (size > 0)
        {
            _cache.Set(key, size, TimeSpan.FromHours(1));
        }

        return size;
    }

    private async Task<IReadOnlyList<int>> GetPieceStatesAsync(string hash, CancellationToken cancellationToken)
    {
        var key = "rutracker:pieces:" + hash;
        if (_cache.TryGetValue(key, out IReadOnlyList<int>? states) && states is not null)
        {
            return states;
        }

        states = await _qbittorrent.GetPieceStatesAsync(hash, cancellationToken).ConfigureAwait(false);
        _cache.Set(key, states, TimeSpan.FromSeconds(1));
        return states;
    }
}
