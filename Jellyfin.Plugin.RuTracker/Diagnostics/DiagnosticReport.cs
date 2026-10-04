using System.Collections.Generic;

namespace Jellyfin.Plugin.RuTracker.Diagnostics;

/// <summary>
/// Result of a connection check.
/// </summary>
/// <param name="Success">Whether the service is usable with the saved settings.</param>
/// <param name="Steps">Performed steps in order.</param>
public sealed record DiagnosticReport(bool Success, IReadOnlyList<DiagnosticStep> Steps);
