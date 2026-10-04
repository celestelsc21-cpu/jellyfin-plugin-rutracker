using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RuTracker.Api.Models;
using Jellyfin.Plugin.RuTracker.Configuration;
using Jellyfin.Plugin.RuTracker.Tracker;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.RuTracker.Api;

/// <summary>
/// Administrative endpoints (server administrators only).
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("RuTracker/Admin")]
[Produces(MediaTypeNames.Application.Json)]
public class AdminController : ControllerBase
{
    private readonly IPluginConfigurationAccessor _config;
    private readonly IRuTrackerClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminController"/> class.
    /// </summary>
    /// <param name="config">Configuration accessor.</param>
    /// <param name="client">RuTracker client.</param>
    public AdminController(IPluginConfigurationAccessor config, IRuTrackerClient client)
    {
        _config = config;
        _client = client;
    }

    /// <summary>
    /// Validates the saved configuration, including folder visibility from
    /// the Jellyfin container, and shows how each folder maps to qBittorrent.
    /// </summary>
    /// <response code="200">Validation report.</response>
    /// <returns>The validation report with mapped folders.</returns>
    [HttpGet("Validate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ValidationReport> Validate()
    {
        var config = _config.Current;
        var result = ConfigurationValidator.Validate(config, Directory.Exists);
        var mapper = new PathMapper(config.PathMappings ?? []);

        var folders = (config.DownloadTargets ?? [])
            .Select(t => new MappedFolder(
                t.Name,
                ConfigurationValidator.KindName(t.Kind),
                t.JellyfinPath,
                mapper.TryToQBittorrent(t.JellyfinPath, out var remote) ? remote : null))
            .ToList();

        return new ValidationReport(result.IsValid, result.Errors, result.Warnings, folders);
    }

    /// <summary>
    /// Checks that the server can log in to RuTracker with the saved settings.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Login succeeded.</response>
    /// <response code="502">Login failed; the message explains why.</response>
    /// <returns>A status message.</returns>
    [HttpPost("TestRuTracker")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<MessageDto>> TestRuTracker(CancellationToken cancellationToken)
    {
        try
        {
            await _client.CheckLoginAsync(cancellationToken).ConfigureAwait(false);
            return new MessageDto("Вход на RuTracker выполнен успешно.");
        }
        catch (RuTrackerException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new MessageDto(ex.Message));
        }
    }
}
