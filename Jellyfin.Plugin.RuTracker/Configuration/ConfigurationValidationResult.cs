using System.Collections.Generic;

namespace Jellyfin.Plugin.RuTracker.Configuration;

/// <summary>
/// Result of configuration validation.
/// </summary>
/// <param name="Errors">Blocking problems.</param>
/// <param name="Warnings">Non-blocking remarks.</param>
public sealed record ConfigurationValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Gets a value indicating whether the configuration has no errors.
    /// </summary>
    public bool IsValid => Errors.Count == 0;
}
