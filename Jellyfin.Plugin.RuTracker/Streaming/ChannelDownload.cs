using System;
using System.Collections.Generic;
using Jellyfin.Plugin.RuTracker.Configuration;

namespace Jellyfin.Plugin.RuTracker.Streaming;

/// <summary>
/// An active download shown as a folder in the RuTracker channel.
/// </summary>
/// <param name="Id">Download id.</param>
/// <param name="Title">Topic title.</param>
/// <param name="Kind">Media kind.</param>
/// <param name="Created">When the download started.</param>
/// <param name="ImageUrl">Poster of the matched movie or series.</param>
/// <param name="Overview">Description of the match.</param>
/// <param name="ProviderIds">Provider ids of the match.</param>
public sealed record ChannelDownload(
    Guid Id,
    string Title,
    MediaKind Kind,
    DateTimeOffset Created,
    string? ImageUrl,
    string? Overview,
    IReadOnlyDictionary<string, string> ProviderIds);
