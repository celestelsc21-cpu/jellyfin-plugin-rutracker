using System;
using Jellyfin.Plugin.RuTracker.Configuration;

namespace Jellyfin.Plugin.RuTracker.Tracker;

/// <summary>
/// Decides the media kind of a RuTracker forum.
/// </summary>
internal static class ForumClassifier
{
    /// <summary>
    /// Classifies a forum by id, falling back to its name for forums missing from <see cref="ForumMap"/>.
    /// </summary>
    /// <param name="forumId">Forum id.</param>
    /// <param name="forumName">Forum display name.</param>
    /// <returns>Media kind, or <c>null</c> for non-video forums.</returns>
    public static MediaKind? Classify(int forumId, string? forumName)
        => ForumMap.Kinds.TryGetValue(forumId, out var kind) ? kind : ClassifyByName(forumName);

    /// <summary>
    /// Name-based heuristic for forums unknown to the static map (new forums appear over time).
    /// </summary>
    /// <param name="forumName">Forum display name.</param>
    /// <returns>Media kind, or <c>null</c> when the name does not look like video content.</returns>
    public static MediaKind? ClassifyByName(string? forumName)
    {
        if (string.IsNullOrWhiteSpace(forumName))
        {
            return null;
        }

        static bool Has(string text, string part) => text.Contains(part, StringComparison.OrdinalIgnoreCase);

        if (Has(forumName, "сериал") || Has(forumName, "аниме"))
        {
            return MediaKind.Series;
        }

        // Documentary sub-forums are named "[Док] ..." and do not always contain "документ".
        if (forumName.StartsWith("[Док", StringComparison.OrdinalIgnoreCase) || Has(forumName, "документал"))
        {
            return MediaKind.Show;
        }

        if (Has(forumName, "передач") || Has(forumName, "шоу") || Has(forumName, "документ") || Has(forumName, "спорт"))
        {
            return MediaKind.Show;
        }

        if (Has(forumName, "кино") || Has(forumName, "фильм") || Has(forumName, "мульт"))
        {
            return MediaKind.Movie;
        }

        return null;
    }
}
