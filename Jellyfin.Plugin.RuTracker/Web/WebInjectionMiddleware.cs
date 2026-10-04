using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.RuTracker.Web;

/// <summary>
/// Adds the plugin header script to the Jellyfin web client index page
/// (<c>/web/</c> and <c>/web/index.html</c>). Every other request passes
/// through untouched. On any problem the original page is served.
/// </summary>
internal sealed class WebInjectionMiddleware
{
    private static readonly string ScriptTag = HtmlInjector.ScriptTag(
        typeof(WebInjectionMiddleware).Assembly.GetName().Version?.ToString() ?? "0");

    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebInjectionMiddleware"/> class.
    /// </summary>
    /// <param name="next">Next middleware.</param>
    public WebInjectionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Processes a request.
    /// </summary>
    /// <param name="context">HTTP context.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>A task.</returns>
    public async Task InvokeAsync(HttpContext context, ILogger<WebInjectionMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsIndexRequest(context.Request))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Ask for a plain, complete response we can edit.
        context.Request.Headers.Remove(HeaderNames.AcceptEncoding);
        context.Request.Headers.Remove(HeaderNames.IfNoneMatch);
        context.Request.Headers.Remove(HeaderNames.IfModifiedSince);

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        var bytes = buffer.ToArray();
        if (context.Response.StatusCode == StatusCodes.Status200OK
            && (context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) ?? false)
            && !context.Response.Headers.ContainsKey(HeaderNames.ContentEncoding))
        {
            try
            {
                var html = Encoding.UTF8.GetString(bytes);
                var injected = HtmlInjector.Inject(html, ScriptTag);
                if (!ReferenceEquals(html, injected))
                {
                    bytes = Encoding.UTF8.GetBytes(injected);
                    context.Response.Headers.Remove(HeaderNames.ETag);
                    context.Response.Headers.Remove(HeaderNames.LastModified);
                    context.Response.Headers.CacheControl = "no-cache";
                }
            }
#pragma warning disable CA1031 // Never break the web client because of the button.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogWarning(ex, "Could not add the RuTracker button to the web client");
            }
        }

        context.Response.ContentLength = bytes.Length;
        await originalBody.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }

    private static bool IsIndexRequest(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method))
        {
            return false;
        }

        var path = (request.PathBase + request.Path).Value ?? string.Empty;
        return path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase);
    }
}
