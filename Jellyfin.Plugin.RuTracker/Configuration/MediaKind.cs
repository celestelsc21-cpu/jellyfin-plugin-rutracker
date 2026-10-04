namespace Jellyfin.Plugin.RuTracker.Configuration;

/// <summary>
/// Kind of media a download belongs to.
/// </summary>
public enum MediaKind
{
    /// <summary>
    /// Feature film (single release or a part of a franchise).
    /// </summary>
    Movie = 0,

    /// <summary>
    /// TV series with seasons and episodes.
    /// </summary>
    Series = 1,

    /// <summary>
    /// TV show / programme (episodic, often without strict seasons).
    /// </summary>
    Show = 2
}
