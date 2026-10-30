using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RuTracker.Api.Models;
using Jellyfin.Plugin.RuTracker.Configuration;
using Jellyfin.Plugin.RuTracker.Library;
using Jellyfin.Plugin.RuTracker.Tracker;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RuTracker.Metadata;

/// <summary>
/// Searches movies and series through Jellyfin's own metadata providers (whatever is enabled
/// on the server: TMDb, TVDB, OMDb...). Found matches are kept on the server for a while so
/// a download request only carries a key: names and image URLs are never taken from the client.
/// </summary>
internal sealed class MetadataMatcher : IMetadataMatcher
{
    private const int MaxResults = 8;
    private static readonly TimeSpan SearchCacheTime = TimeSpan.FromHours(1);
    private static readonly TimeSpan MatchCacheTime = TimeSpan.FromHours(12);

    private readonly IProviderManager _providers;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MetadataMatcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataMatcher"/> class.
    /// </summary>
    /// <param name="providers">Jellyfin provider manager.</param>
    /// <param name="cache">Memory cache.</param>
    /// <param name="logger">Logger.</param>
    public MetadataMatcher(IProviderManager providers, IMemoryCache cache, ILogger<MetadataMatcher> logger)
    {
        _providers = providers;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MetadataMatchDto>> SuggestAsync(MediaKind kind, string releaseTitle, CancellationToken cancellationToken)
    {
        var (names, year) = ReleaseTitle.Parse(releaseTitle);

        // Original (Latin) names first: providers match them best.
        var ordered = names
            .OrderBy(n => n.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z') ? 0 : 1)
            .Take(3)
            .ToList();

        var results = new List<MetadataMatchDto>();
        foreach (var name in ordered)
        {
            // The release year of a series is the year of its season, not of the series.
            var found = await SearchAsync(kind, name, kind == MediaKind.Movie ? year : null, cancellationToken).ConfigureAwait(false);
            if (found.Count == 0 && kind == MediaKind.Movie && year is not null)
            {
                found = await SearchAsync(kind, name, null, cancellationToken).ConfigureAwait(false);
            }

            results.AddRange(found.Where(f => results.All(r => r.Key != f.Key)));
            if (results.Count >= 3)
            {
                break;
            }
        }

        return results.Take(MaxResults).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MetadataMatchDto>> SearchAsync(MediaKind kind, string name, int? year, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        name = name.Trim();
        var cacheKey = string.Create(CultureInfo.InvariantCulture, $"rutracker:meta:{kind}:{year}:{name.ToLowerInvariant()}");
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<MetadataMatchDto>? cached) && cached is not null)
        {
            return cached;
        }

        IEnumerable<RemoteSearchResult> found;
        try
        {
            found = kind == MediaKind.Movie
                ? await _providers.GetRemoteSearchResults<Movie, MovieInfo>(
                    new RemoteSearchQuery<MovieInfo> { SearchInfo = new MovieInfo { Name = name, Year = year } },
                    cancellationToken).ConfigureAwait(false)
                : await _providers.GetRemoteSearchResults<Series, SeriesInfo>(
                    new RemoteSearchQuery<SeriesInfo> { SearchInfo = new SeriesInfo { Name = name, Year = year } },
                    cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Provider failures must not break the download dialog.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogWarning("Metadata search failed: {Reason}", ex.Message);
            return [];
        }

        var results = new List<MetadataMatchDto>();
        foreach (var result in found)
        {
            var ids = new Dictionary<string, string>(result.ProviderIds ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            var tag = LibraryLayout.FolderTag(ids);
            if (tag is null || string.IsNullOrWhiteSpace(result.Name))
            {
                continue;
            }

            var key = kind + ":" + tag;
            if (results.Any(r => r.Key == key))
            {
                continue;
            }

            var resultYear = result.ProductionYear ?? result.PremiereDate?.Year;
            var imageUrl = IsHttpUrl(result.ImageUrl) ? result.ImageUrl : null;
            var match = new MetadataMatch(result.Name, resultYear, Shorten(result.Overview), imageUrl, ids, tag);
            _cache.Set(MatchKey(key), match, MatchCacheTime);
            results.Add(new MetadataMatchDto(key, match.Name, match.Year, match.Overview, match.ImageUrl, result.SearchProviderName ?? string.Empty, tag));
            if (results.Count >= MaxResults)
            {
                break;
            }
        }

        _cache.Set(cacheKey, (IReadOnlyList<MetadataMatchDto>)results, SearchCacheTime);
        return results;
    }

    /// <summary>
    /// Gets a match returned earlier by a search.
    /// </summary>
    /// <param name="key">Key from <see cref="MetadataMatchDto.Key"/>.</param>
    /// <returns>The match, or <c>null</c> when unknown or expired.</returns>
    public MetadataMatch? Find(string? key)
        => !string.IsNullOrEmpty(key) && _cache.TryGetValue(MatchKey(key), out MetadataMatch? match) ? match : null;

    private static string MatchKey(string key) => "rutracker:match:" + key;

    private static bool IsHttpUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string? Shorten(string? text)
        => string.IsNullOrWhiteSpace(text) ? null : text.Length <= 400 ? text : text[..400].TrimEnd() + "…";
}
