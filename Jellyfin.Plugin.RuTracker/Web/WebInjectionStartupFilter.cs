using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Jellyfin.Plugin.RuTracker.Web;

/// <summary>
/// Registers <see cref="WebInjectionMiddleware"/> at the start of the server pipeline.
/// </summary>
/// <remarks>
/// Jellyfin has no public extension point for the web client header; this is
/// the same technique the community File Transformation plugin uses on 10.11.
/// </remarks>
internal sealed class WebInjectionStartupFilter : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<WebInjectionMiddleware>();
            next(app);
        };
    }
}
