using Jellyfin.Plugin.RuTracker.Configuration;

namespace Jellyfin.Plugin.RuTracker.Downloads;

/// <summary>
/// User-facing Russian messages about downloads (grammatical gender follows the media kind).
/// </summary>
internal static class DownloadMessages
{
    /// <summary>
    /// "Сериал «…» будет загружен" / "Передача «…» будет загружена".
    /// </summary>
    /// <param name="kind">Media kind.</param>
    /// <param name="title">Title.</param>
    /// <returns>Message.</returns>
    public static string WillBeDownloaded(MediaKind kind, string title)
        => ConfigurationValidator.KindName(kind) + " «" + title + "» будет " + (kind == MediaKind.Show ? "загружена" : "загружен") + ".";

    /// <summary>
    /// "Сериал «…» уже загружается".
    /// </summary>
    /// <param name="kind">Media kind.</param>
    /// <param name="title">Title.</param>
    /// <returns>Message.</returns>
    public static string AlreadyDownloading(MediaKind kind, string title)
        => ConfigurationValidator.KindName(kind) + " «" + title + "» уже загружается.";
}
