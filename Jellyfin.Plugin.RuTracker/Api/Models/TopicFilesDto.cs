using System.Collections.Generic;

namespace Jellyfin.Plugin.RuTracker.Api.Models;

/// <summary>
/// Files of a RuTracker topic's torrent.
/// </summary>
/// <param name="TopicId">Topic id.</param>
/// <param name="Title">Topic title.</param>
/// <param name="Files">Video files in watching order (empty when only a magnet link is available).</param>
/// <param name="FolderName">Folder the download will be placed in, e.g. "Title (2006)".</param>
/// <param name="FileListKnown">The file list is known (a .torrent file was available).</param>
public sealed record TopicFilesDto(long TopicId, string Title, IReadOnlyList<TopicFileDto> Files, string FolderName, bool FileListKnown);
