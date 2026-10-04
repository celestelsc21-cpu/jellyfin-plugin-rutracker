using System.IO;
using System.Linq;
using System.Net.Mime;
using Jellyfin.Plugin.RuTracker.Api.Models;
using Jellyfin.Plugin.RuTracker.Configuration;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminController"/> class.
    /// </summary>
    /// <param name="config">Configuration accessor.</param>
    public AdminController(IPluginConfigurationAccessor config)
    {
        _config = config;
    }

    /// <summary>
    /// Validates the saved configuration, including folder visibility from
    /// the Jellyfin container, and shows how each folder maps to qBittorrent.
    /// </summary>
    /// <response code="200">Validation report.</response>
    /// <returns>Validation report.</returns>
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
}
