using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RuTracker.Access;
using Jellyfin.Plugin.RuTracker.Api.Models;
using Jellyfin.Plugin.RuTracker.Configuration;
using Jellyfin.Plugin.RuTracker.Diagnostics;
using Jellyfin.Plugin.RuTracker.QBittorrent;
using Jellyfin.Plugin.RuTracker.Tracker;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Library;
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
    private readonly IQBittorrentClient _qbittorrent;
    private readonly IChannelManager _channels;
    private readonly IUserManager _users;
    private readonly IAccessService _access;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminController"/> class.
    /// </summary>
    /// <param name="config">Configuration accessor.</param>
    /// <param name="client">RuTracker client.</param>
    /// <param name="qbittorrent">qBittorrent client.</param>
    /// <param name="channels">Channel manager.</param>
    /// <param name="users">User manager.</param>
    /// <param name="access">Access service.</param>
    public AdminController(
        IPluginConfigurationAccessor config,
        IRuTrackerClient client,
        IQBittorrentClient qbittorrent,
        IChannelManager channels,
        IUserManager users,
        IAccessService access)
    {
        _channels = channels;
        _users = users;
        _access = access;
        _config = config;
        _client = client;
        _qbittorrent = qbittorrent;
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
    /// Checks the RuTracker connection step by step for the main and alternative addresses.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Check performed; see <see cref="DiagnosticReport.Success"/>.</response>
    /// <returns>The step-by-step report.</returns>
    [HttpPost("TestRuTracker")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<DiagnosticReport>> TestRuTracker(CancellationToken cancellationToken)
        => await _client.DiagnoseAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Checks the qBittorrent connection step by step.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Check performed; see <see cref="DiagnosticReport.Success"/>.</response>
    /// <returns>The step-by-step report.</returns>
    [HttpPost("TestQBittorrent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<DiagnosticReport>> TestQBittorrent(CancellationToken cancellationToken)
        => await _qbittorrent.DiagnoseAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Checks the "RuTracker" channel and write access to the download folders.
    /// </summary>
    /// <returns>Step-by-step report.</returns>
    /// <response code="200">Report returned.</response>
    [HttpPost("TestLibrary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<DiagnosticReport>> TestLibrary()
        => await LibraryDiagnostics.RunAsync(_config.Current, _channels, _users, _access).ConfigureAwait(false);

    /// <summary>
    /// Allows the "RuTracker" channel for every user with the search role.
    /// </summary>
    /// <returns>A user-facing message.</returns>
    /// <response code="200">Message returned.</response>
    [HttpPost("AllowChannel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<MessageDto>> AllowChannel()
        => new MessageDto(await LibraryDiagnostics.AllowChannelAsync(_channels, _users, _access).ConfigureAwait(false));
}
