using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Jellyfin.Plugin.RuTracker.Library;

/// <summary>
/// Builds the contents of a Jellyfin <c>.ignore</c> file (gitignore syntax, Jellyfin 10.11+)
/// that hides unfinished files from the library scanner.
/// </summary>
internal static class IgnoreFile
{
    /// <summary>
    /// File name Jellyfin looks for.
    /// </summary>
    public const string FileName = ".ignore";

    /// <summary>
    /// First line of every file written by the plugin.
    /// </summary>
    public const string Header = "# Managed by the RuTracker plugin: files still downloading.";

    /// <summary>
    /// Gets a value indicating whether an existing <c>.ignore</c> was written by the plugin.
    /// </summary>
    /// <param name="content">File content.</param>
    /// <returns><c>true</c> when the plugin may change or delete it.</returns>
    public static bool IsManaged(string content)
        => content?.StartsWith(Header, StringComparison.Ordinal) ?? false;

    /// <summary>
    /// Builds rules that match exactly the given files by name (in any subfolder).
    /// </summary>
    /// <param name="relativePaths">Paths of the files to hide.</param>
    /// <returns>File content, or <c>null</c> when nothing needs hiding (the file must then be removed,
    /// because an empty <c>.ignore</c> hides the whole folder).</returns>
    public static string? Build(IEnumerable<string> relativePaths)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);
        var rules = relativePaths
            .Select(p => Path.GetFileName(p.Replace('\\', '/')))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .Select(Escape)
            .ToList();

        if (rules.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder(Header).Append('\n');
        foreach (var rule in rules)
        {
            builder.Append(rule).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Escapes gitignore pattern characters so the name matches literally.
    /// </summary>
    /// <param name="name">File name.</param>
    /// <returns>Literal pattern.</returns>
    public static string Escape(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var builder = new StringBuilder(name.Length + 4);
        foreach (var c in name)
        {
            if (c is '\\' or '*' or '?' or '[' or ']' or '!' or '#')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        // Trailing spaces are dropped by gitignore unless escaped.
        var result = builder.ToString();
        var trimmed = result.TrimEnd(' ');
        return trimmed.Length == result.Length ? result : trimmed + string.Concat(Enumerable.Repeat("\\ ", result.Length - trimmed.Length));
    }
}
