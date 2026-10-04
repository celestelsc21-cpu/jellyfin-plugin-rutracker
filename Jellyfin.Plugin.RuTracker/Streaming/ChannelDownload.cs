using System;
using Jellyfin.Plugin.RuTracker.Configuration;

namespace Jellyfin.Plugin.RuTracker.Streaming;

/// <summary>
/// An active download shown as a folder in the RuTracker channel.
/// </summary>
/// <param name="Id">Download id.</param>
/// <param name="Title">Topic title.</param>
/// <param name="Kind">Media kind.</param>
/// <param name="Created">When the download started.</param>
public sealed record ChannelDownload(Guid Id, string Title, MediaKind Kind, DateTimeOffset Created);
