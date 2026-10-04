using System;

namespace Jellyfin.Plugin.RuTracker.Web;

/// <summary>
/// Inserts the plugin's header script into the web client's index page.
/// Pure function: idempotent and safe on unexpected markup.
/// </summary>
internal static class HtmlInjector
{
    /// <summary>
    /// Marker attribute that makes the injection idempotent.
    /// </summary>
    public const string Marker = "data-rutracker-plugin";

    /// <summary>
    /// Builds the script tag. The path is relative to <c>/web/</c>, so it keeps
    /// working when Jellyfin runs under a base URL such as <c>/jellyfin</c>.
    /// </summary>
    /// <param name="version">Plugin version, used to bust the browser cache.</param>
    /// <returns>The script tag.</returns>
    public static string ScriptTag(string version)
        => $"<script src=\"../RuTracker/Web/header.js?v={Uri.EscapeDataString(version)}\" defer {Marker}></script>";

    /// <summary>
    /// Inserts <paramref name="tag"/> right before the last <c>&lt;/body&gt;</c>.
    /// </summary>
    /// <param name="html">Original HTML.</param>
    /// <param name="tag">Tag to insert.</param>
    /// <returns>Modified HTML, or the original when there is no body or the tag is already present.</returns>
    public static string Inject(string html, string tag)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(tag);

        if (html.Contains(Marker, StringComparison.Ordinal))
        {
            return html;
        }

        var index = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? html : html.Insert(index, tag);
    }
}
