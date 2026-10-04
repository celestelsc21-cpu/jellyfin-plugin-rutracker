using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.RuTracker.Web;

/// <summary>
/// Serves the plugin's static web assets: the search page and the header button script.
/// </summary>
/// <remarks>
/// Anonymous on purpose: a browser cannot attach the Jellyfin token when it
/// navigates to a page or loads a script tag. These files contain no data and
/// no secrets; every data request they make goes to role-checked endpoints.
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("RuTracker/Web")]
[ApiExplorerSettings(IgnoreApi = true)]
public class WebController : ControllerBase
{
    private const string ResourcePrefix = "Jellyfin.Plugin.RuTracker.Web.";

    /// <summary>
    /// Gets the search page.
    /// </summary>
    /// <returns>The HTML page.</returns>
    [HttpGet("")]
    public ActionResult GetSearchPage() => Resource("search.html", "text/html; charset=utf-8");

    /// <summary>
    /// Gets the script that adds the RuTracker button to the web client header.
    /// </summary>
    /// <returns>The JavaScript file.</returns>
    [HttpGet("header.js")]
    public ActionResult GetHeaderScript() => Resource("header.js", "application/javascript; charset=utf-8");

    private FileStreamResult Resource(string name, string contentType)
    {
        Stream stream = typeof(WebController).Assembly.GetManifestResourceStream(ResourcePrefix + name)
            ?? throw new FileNotFoundException("Embedded resource missing", name);

        Response.Headers.CacheControl = "no-cache";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(stream, contentType);
    }
}
