using System.Collections.Generic;

namespace Jellyfin.Plugin.RuTracker.Api.Models;

/// <summary>
/// Files of a RuTracker topic's torrent.
/// </summary>
/// <param name="TopicId">Topic id.</param>
/// <param name="Title">Topic title.</param>
/// <param name="Files">Video files in watching order (empty when only a magnet link is available).</param>
public sealed record TopicFilesDto(long TopicId, string Title, IReadOnlyList<TopicFileDto> Files);
