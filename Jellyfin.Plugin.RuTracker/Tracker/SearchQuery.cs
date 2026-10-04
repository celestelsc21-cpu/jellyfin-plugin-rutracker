using System.Text;

namespace Jellyfin.Plugin.RuTracker.Tracker;

/// <summary>
/// Normalizes user search input before it is sent to RuTracker.
/// </summary>
internal static class SearchQuery
{
    /// <summary>
    /// Minimum query length after normalization.
    /// </summary>
    public const int MinLength = 2;

    /// <summary>
    /// Maximum query length after normalization.
    /// </summary>
    public const int MaxLength = 100;

    /// <summary>
    /// Keeps letters and digits, turns everything else into single spaces.
    /// </summary>
    /// <param name="query">Raw user input.</param>
    /// <returns>Normalized query, or <c>null</c> if it is too short.</returns>
    public static string? Normalize(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var builder = new StringBuilder(query.Length);
        var lastWasSpace = true;
        foreach (var c in query)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        var normalized = builder.ToString().Trim();
        if (normalized.Length > MaxLength)
        {
            normalized = normalized[..MaxLength].TrimEnd();
        }

        return normalized.Length >= MinLength ? normalized : null;
    }
}
