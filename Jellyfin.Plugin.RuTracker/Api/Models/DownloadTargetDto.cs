using System;
using Jellyfin.Plugin.RuTracker.Configuration;

namespace Jellyfin.Plugin.RuTracker.Api.Models;

/// <summary>
/// A download destination offered to the user (paths are not exposed).
/// </summary>
/// <param name="Id">Target id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Kind">Media kind the folder is meant for.</param>
/// <param name="IsDefault">Default folder for its kind.</param>
public sealed record DownloadTargetDto(Guid Id, string Name, MediaKind Kind, bool IsDefault);
