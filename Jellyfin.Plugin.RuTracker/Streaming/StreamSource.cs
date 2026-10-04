namespace Jellyfin.Plugin.RuTracker.Streaming;

/// <summary>
/// Where to read a (possibly still downloading) file from.
/// </summary>
/// <param name="Path">Full path inside the Jellyfin container.</param>
/// <param name="Size">Final file size in bytes.</param>
/// <param name="InfoHash">Torrent info hash.</param>
/// <param name="FirstPiece">First torrent piece of the file.</param>
/// <param name="LastPiece">Last torrent piece of the file.</param>
/// <param name="Complete">The file is fully downloaded.</param>
public sealed record StreamSource(string Path, long Size, string InfoHash, int FirstPiece, int LastPiece, bool Complete);
