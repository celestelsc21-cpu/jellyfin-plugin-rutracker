namespace Jellyfin.Plugin.RuTracker.Diagnostics;

/// <summary>
/// One step of a connection check.
/// </summary>
/// <param name="Name">Short step name, e.g. "rutracker.org: соединение".</param>
/// <param name="Ok">Whether the step succeeded.</param>
/// <param name="Detail">User-facing explanation; never contains secrets.</param>
public sealed record DiagnosticStep(string Name, bool Ok, string Detail);
