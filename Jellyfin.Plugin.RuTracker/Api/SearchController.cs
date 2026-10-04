using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RuTracker.Access;
using Jellyfin.Plugin.RuTracker.Api.Models;
using Jellyfin.Plugin.RuTracker.Configuration;
using Jellyfin.Plugin.RuTracker.Tracker;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.RuTracker.Api;

/// <summary>
/// RuTracker search for users with the Search role.
/// </summary>
[ApiController]
[Authorize]
[Route("RuTracker")]
[Produces(MediaTypeNames.Application.Json)]
public class SearchController : ControllerBase
{
    private readonly IRuTrackerClient _client;
    private readonly IPluginConfigurationAccessor _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchController"/> class.
    /// </summary>
    /// <param name="client">RuTracker client.</param>
    /// <param name="config">Configuration accessor.</param>
    public SearchController(IRuTrackerClient client, IPluginConfigurationAccessor config)
    {
        _client = client;
        _config = config;
    }

    /// <summary>
    /// Searches RuTracker for video content.
    /// </summary>
    /// <param name="query">Search text, 2–100 characters.</param>
    /// <param name="kind">Optional media kind filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Results, most seeded first.</response>
    /// <response code="400">The query is too short.</response>
    /// <response code="502">RuTracker is unreachable or rejected the login.</response>
    /// <returns>Found torrents.</returns>
    [HttpGet("Search")]
    [RequireRuTrackerRole(AccessRole.Search)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<SearchItemDto>>> Search(
        [FromQuery, Required] string query,
        [FromQuery] MediaKind? kind,
        CancellationToken cancellationToken)
    {
        var normalized = SearchQuery.Normalize(query);
        if (normalized is null)
        {
            return BadRequest(new MessageDto("Введите хотя бы два символа."));
        }

        IReadOnlyList<TorrentInfo> torrents;
        try
        {
            torrents = await _client.SearchAsync(normalized, cancellationToken).ConfigureAwait(false);
        }
        catch (RuTrackerException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new MessageDto(ex.Message));
        }

        var baseUrl = _config.Current.RuTrackerBaseUrl.TrimEnd('/');
        var items = torrents
            .Where(t => t.Kind is not null && (kind is null || t.Kind == kind))
            .OrderByDescending(t => t.Seeders)
            .Select(t => new SearchItemDto(
                t.TopicId,
                t.Title,
                t.Kind!.Value,
                t.ForumName,
                t.SizeBytes,
                t.Seeders,
                t.Leechers,
                t.Downloads,
                t.Added,
                new Uri(baseUrl + "/forum/viewtopic.php?t=" + t.TopicId.ToString(CultureInfo.InvariantCulture))))
            .ToList();

        return Ok(items);
    }
}
