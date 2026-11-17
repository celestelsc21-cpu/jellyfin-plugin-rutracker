namespace Jellyfin.Plugin.RuTracker.Library;

/// <summary>
/// An index-preserving torrent file rename.
/// </summary>
/// <param name="Index">Torrent file index.</param>
/// <param name="OldPath">Current path inside the torrent.</param>
/// <param name="NewPath">Proposed path inside the torrent.</param>
internal sealed record EpisodeFileRename(int Index, string OldPath, string NewPath);
