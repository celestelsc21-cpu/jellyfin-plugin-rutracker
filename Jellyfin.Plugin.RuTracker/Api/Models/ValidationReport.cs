using System.Collections.Generic;

namespace Jellyfin.Plugin.RuTracker.Api.Models;

/// <summary>
/// Configuration validation report.
/// </summary>
/// <param name="IsValid">No blocking errors.</param>
/// <param name="Errors">Blocking errors.</param>
/// <param name="Warnings">Warnings.</param>
/// <param name="Folders">Download folders with their qBittorrent paths.</param>
public sealed record ValidationReport(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<MappedFolder> Folders);
