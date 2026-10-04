using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RuTracker.Access;
using Jellyfin.Plugin.RuTracker.Configuration;
using Jellyfin.Plugin.RuTracker.Downloads;
using Jellyfin.Plugin.RuTracker.QBittorrent;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RuTracker.Streaming;

/// <summary>
/// "RuTracker" channel: lists the downloads in progress and plays their episodes while they
/// are still downloading, through the plugin's stream endpoint.
/// </summary>
/// <remarks>
/// Media sources are produced on demand (<see cref="IRequiresMediaInfoCallback"/>) because
/// stream links are signed and expire. The channel deliberately does not implement
/// <c>ISupportsMediaProbe</c>: probing while listing would read half-downloaded files.
/// </remarks>
internal sealed class RuTrackerChannel : IChannel, IRequiresMediaInfoCallback, IHasCacheKey
{
    private const string FolderPrefix = "d";
    private const string FilePrefix = "f";

    private readonly IDownloadManager _manager;
    private readonly IAccessService _access;
    private readonly StreamTokens _tokens;
    private readonly IServerApplicationHost _host;
    private readonly StreamProbe _probe;
    private readonly ILogger<RuTrackerChannel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RuTrackerChannel"/> class.
    /// </summary>
    /// <param name="manager">Download manager.</param>
    /// <param name="access">Access service.</param>
    /// <param name="tokens">Stream URL signer.</param>
    /// <param name="host">Server host (local API URL).</param>
    /// <param name="probe">Stream prober.</param>
    /// <param name="logger">Logger.</param>
    public RuTrackerChannel(
        IDownloadManager manager,
        IAccessService access,
        StreamTokens tokens,
        IServerApplicationHost host,
        StreamProbe probe,
        ILogger<RuTrackerChannel> logger)
    {
        _manager = manager;
        _access = access;
        _tokens = tokens;
        _host = host;
        _probe = probe;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "RuTracker";

    /// <inheritdoc />
    public string Description => "Просмотр раздач RuTracker во время загрузки";

    /// <inheritdoc />
    public string DataVersion => "1";

    /// <inheritdoc />
    public string HomePageUrl => "https://github.com/celestelsc21-cpu/jellyfin-plugin-rutracker";

    /// <inheritdoc />
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    /// <inheritdoc />
    public InternalChannelFeatures GetChannelFeatures()
    {
        var features = new InternalChannelFeatures();
        features.MediaTypes.Add(ChannelMediaType.Video);
        features.ContentTypes.Add(ChannelMediaContentType.Movie);
        features.ContentTypes.Add(ChannelMediaContentType.Episode);
        return features;
    }

    /// <inheritdoc />
    public bool IsEnabledFor(string userId)
        => Guid.TryParse(userId, out var id) && _access.GetAccess(id).CanSearch;

    /// <inheritdoc />
    public string? GetCacheKey(string? userId)
    {
        // Progress changes constantly: let Jellyfin's 3-hour channel cache expire every minute.
        return (DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60).ToString(CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            var items = TryParseFolder(query.FolderId, out var downloadId)
                ? await GetFilesAsync(downloadId, cancellationToken).ConfigureAwait(false)
                : await GetFoldersAsync(cancellationToken).ConfigureAwait(false);
            return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
        }
        catch (Exception ex) when (ex is QBittorrentException or DownloadException or IOException)
        {
            _logger.LogWarning("RuTracker channel could not list downloads: {Reason}", ex.Message);
            return new ChannelItemResult();
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
    {
        if (!TryParseFile(id, out var downloadId, out var fileIndex))
        {
            return [];
        }

        try
        {
            var source = await _manager.GetStreamSourceAsync(downloadId, fileIndex, cancellationToken).ConfigureAwait(false);
            if (source is null)
            {
                return [];
            }

            await _manager.EnsureWatchingAsync(downloadId, fileIndex, cancellationToken).ConfigureAwait(false);
            var mediaSource = CreateMediaSource(id, downloadId, fileIndex, source);
            await _probe.ApplyAsync(mediaSource, id, cancellationToken).ConfigureAwait(false);
            return [mediaSource];
        }
        catch (Exception ex) when (ex is QBittorrentException or DownloadException or IOException)
        {
            _logger.LogWarning("RuTracker channel could not prepare playback: {Reason}", ex.Message);
            return [];
        }
    }

    /// <inheritdoc />
    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult(new DynamicImageResponse { HasImage = false });

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedChannelImages() => [];

    /// <summary>
    /// Builds the item id of a file.
    /// </summary>
    /// <param name="downloadId">Download id.</param>
    /// <param name="fileIndex">File index.</param>
    /// <returns>Channel item id.</returns>
    internal static string FileId(Guid downloadId, int fileIndex)
        => string.Create(CultureInfo.InvariantCulture, $"{FilePrefix}{downloadId:N}-{fileIndex}");

    /// <summary>
    /// Parses a file item id.
    /// </summary>
    /// <param name="id">Channel item id.</param>
    /// <param name="downloadId">Download id.</param>
    /// <param name="fileIndex">File index.</param>
    /// <returns><c>true</c> when the id is a file id.</returns>
    internal static bool TryParseFile(string? id, out Guid downloadId, out int fileIndex)
    {
        downloadId = Guid.Empty;
        fileIndex = -1;
        if (string.IsNullOrEmpty(id) || !id.StartsWith(FilePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var dash = id.IndexOf('-', StringComparison.Ordinal);
        return dash > 1
            && Guid.TryParseExact(id[1..dash], "N", out downloadId)
            && int.TryParse(id[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out fileIndex);
    }

    /// <summary>
    /// Parses a folder item id.
    /// </summary>
    /// <param name="id">Channel folder id.</param>
    /// <param name="downloadId">Download id.</param>
    /// <returns><c>true</c> when the id is a download folder id.</returns>
    internal static bool TryParseFolder(string? id, out Guid downloadId)
    {
        downloadId = Guid.Empty;
        return !string.IsNullOrEmpty(id)
            && id.StartsWith(FolderPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(id[1..], "N", out downloadId);
    }

    private static string FolderId(Guid downloadId)
        => FolderPrefix + downloadId.ToString("N", CultureInfo.InvariantCulture);

    private async Task<List<ChannelItemInfo>> GetFoldersAsync(CancellationToken cancellationToken)
    {
        var downloads = await _manager.GetChannelDownloadsAsync(cancellationToken).ConfigureAwait(false);
        return downloads.Select(d => new ChannelItemInfo
        {
            Id = FolderId(d.Id),
            Name = d.Title,
            Type = ChannelItemType.Folder,
            FolderType = d.Kind == MediaKind.Movie ? ChannelFolderType.Container : ChannelFolderType.Series,
            DateCreated = d.Created.UtcDateTime,
            DateModified = d.Created.UtcDateTime
        }).ToList();
    }

    private async Task<List<ChannelItemInfo>> GetFilesAsync(Guid downloadId, CancellationToken cancellationToken)
    {
        var download = (await _manager.GetChannelDownloadsAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(d => d.Id == downloadId);
        var files = await _manager.GetChannelFilesAsync(downloadId, cancellationToken).ConfigureAwait(false);
        var isMovie = download?.Kind == MediaKind.Movie && files.Count == 1;
        return files.Select(f => new ChannelItemInfo
        {
            Id = FileId(downloadId, f.Index),
            Name = string.Create(CultureInfo.InvariantCulture, $"{f.Number}. {f.Label}"),
            SeriesName = isMovie ? null : download?.Title,
            Overview = ProgressNote(f.Progress),
            Type = ChannelItemType.Media,
            MediaType = ChannelMediaType.Video,
            ContentType = isMovie ? ChannelMediaContentType.Movie : ChannelMediaContentType.Episode,
            IndexNumber = f.Number,
            DateModified = DateTime.UtcNow
        }).ToList();
    }

    private static string ProgressNote(double progress)
        => progress >= 1
            ? "Загружено полностью"
            : string.Create(CultureInfo.InvariantCulture, $"Загружено {Math.Floor(progress * 100)}%. Можно смотреть: недостающие части докачиваются в первую очередь.");

    private MediaSourceInfo CreateMediaSource(string itemId, Guid downloadId, int fileIndex, StreamSource source)
    {
        var baseUrl = _host.GetLocalApiUrl("127.0.0.1", "http").TrimEnd('/');
        var url = string.Create(CultureInfo.InvariantCulture, $"{baseUrl}/RuTracker/Stream/{downloadId:N}/{fileIndex}?{_tokens.Query(downloadId, fileIndex)}");
        var extension = Path.GetExtension(source.Path.Replace(".!qB", string.Empty, StringComparison.Ordinal)).TrimStart('.').ToLowerInvariant();
        return new MediaSourceInfo
        {
            Id = itemId,
            Name = Path.GetFileName(source.Path),
            Path = url,
            Protocol = MediaProtocol.Http,
            IsRemote = true,
            Container = extension,
            Size = source.Size,
            SupportsDirectPlay = false,
            SupportsDirectStream = true,
            SupportsTranscoding = true,
            SupportsProbing = true
        };
    }
}
