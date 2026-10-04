using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RuTracker.Access;
using Jellyfin.Plugin.RuTracker.Api.Models;
using Jellyfin.Plugin.RuTracker.Downloads;
using Jellyfin.Plugin.RuTracker.QBittorrent;
using Jellyfin.Plugin.RuTracker.Tracker;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.RuTracker.Api;

/// <summary>
/// Downloads through qBittorrent for users with the Download role.
/// </summary>
[ApiController]
[Authorize]
[Route("RuTracker")]
[Produces(MediaTypeNames.Application.Json)]
public class DownloadsController : ControllerBase
{
    private readonly IDownloadManager _manager;
    private readonly IAccessService _access;

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadsController"/> class.
    /// </summary>
    /// <param name="manager">Download manager.</param>
    /// <param name="access">Access service.</param>
    public DownloadsController(IDownloadManager manager, IAccessService access)
    {
        _manager = manager;
        _access = access;
    }

    /// <summary>
    /// Gets the configured download folders.
    /// </summary>
    /// <response code="200">Folders.</response>
    /// <returns>Folders without paths.</returns>
    [HttpGet("Targets")]
    [RequireRuTrackerRole(AccessRole.Download)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<DownloadTargetDto>> GetTargets() => Ok(_manager.GetTargets());

    /// <summary>
    /// Gets the video files of a topic, to choose the episode to start with.
    /// </summary>
    /// <param name="topicId">Topic id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Files.</response>
    /// <response code="502">RuTracker did not provide the torrent.</response>
    /// <returns>Files in watching order.</returns>
    [HttpGet("Topics/{topicId}/Files")]
    [RequireRuTrackerRole(AccessRole.Download)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<TopicFilesDto>> GetTopicFiles([FromRoute, Range(1, long.MaxValue)] long topicId, CancellationToken cancellationToken)
    {
        try
        {
            return await _manager.GetTopicFilesAsync(topicId, cancellationToken).ConfigureAwait(false);
        }
        catch (RuTrackerException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new MessageDto(ex.Message));
        }
    }

    /// <summary>
    /// Starts a download.
    /// </summary>
    /// <param name="request">Request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Started (or already running); see the message.</response>
    /// <response code="400">The request cannot be fulfilled; see the message.</response>
    /// <returns>Result with a user-facing message.</returns>
    [HttpPost("Downloads")]
    [RequireRuTrackerRole(AccessRole.Download)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DownloadCreatedDto>> Create([FromBody, Required] CreateDownloadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _manager.CreateAsync(CallerId(), request, cancellationToken).ConfigureAwait(false);
        }
        catch (DownloadException ex)
        {
            return BadRequest(new MessageDto(ex.Message));
        }
    }

    /// <summary>
    /// Lists downloads.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Downloads.</response>
    /// <returns>Downloads, newest first.</returns>
    [HttpGet("Downloads")]
    [RequireRuTrackerRole(AccessRole.Download)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DownloadDto>>> List(CancellationToken cancellationToken)
        => Ok(await _manager.ListAsync(CallerId(), IsAdministrator(), cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Gets the video files of a download.
    /// </summary>
    /// <param name="id">Download id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Files.</response>
    /// <response code="404">Unknown download.</response>
    /// <returns>Files in watching order.</returns>
    [HttpGet("Downloads/{id}/Files")]
    [RequireRuTrackerRole(AccessRole.Download)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<DownloadFileDto>>> GetFiles([FromRoute] Guid id, CancellationToken cancellationToken)
        => await Guard(async () => Ok(await _manager.GetFilesAsync(id, cancellationToken).ConfigureAwait(false))).ConfigureAwait(false);

    /// <summary>
    /// Changes the episode to watch first.
    /// </summary>
    /// <param name="id">Download id.</param>
    /// <param name="request">Request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">Changed.</response>
    /// <response code="404">Unknown download.</response>
    /// <returns>No content.</returns>
    [HttpPost("Downloads/{id}/Start")]
    [RequireRuTrackerRole(AccessRole.Download)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SetStart([FromRoute] Guid id, [FromBody, Required] SetStartRequest request, CancellationToken cancellationToken)
        => await Guard(async () =>
        {
            await _manager.SetStartAsync(id, request.FileIndex, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }).ConfigureAwait(false);

    /// <summary>
    /// Cancels a download (only its starter or an administrator).
    /// </summary>
    /// <param name="id">Download id.</param>
    /// <param name="deleteFiles">Also delete downloaded files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">Removed.</response>
    /// <response code="403">Not the starter and not an administrator.</response>
    /// <response code="404">Unknown download.</response>
    /// <returns>No content.</returns>
    [HttpDelete("Downloads/{id}")]
    [RequireRuTrackerRole(AccessRole.Download)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete([FromRoute] Guid id, [FromQuery] bool deleteFiles, CancellationToken cancellationToken)
        => await Guard(async () =>
        {
            await _manager.DeleteAsync(id, deleteFiles, CallerId(), IsAdministrator(), cancellationToken).ConfigureAwait(false);
            return NoContent();
        }).ConfigureAwait(false);

    private Guid CallerId() => CallerIdentity.GetUserId(User);

    private bool IsAdministrator()
        => CallerIdentity.IsApiKey(User) || _access.GetAccess(CallerId()).IsAdministrator;

    private async Task<ActionResult> Guard(Func<Task<ActionResult>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new MessageDto(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageDto(ex.Message));
        }
        catch (DownloadException ex)
        {
            return BadRequest(new MessageDto(ex.Message));
        }
        catch (QBittorrentException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new MessageDto(ex.Message));
        }
    }
}
