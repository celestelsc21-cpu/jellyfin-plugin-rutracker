using System;

namespace Jellyfin.Plugin.RuTracker.Api.Models;

/// <summary>
/// Result of starting a download.
/// </summary>
/// <param name="Id">Download id.</param>
/// <param name="Message">User-facing message, e.g. "Сериал «…» будет загружен".</param>
/// <param name="AlreadyExisted">The topic was already being downloaded.</param>
public sealed record DownloadCreatedDto(Guid Id, string Message, bool AlreadyExisted);
