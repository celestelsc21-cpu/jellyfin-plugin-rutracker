using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace Jellyfin.Plugin.RuTracker.Torrents;

/// <summary>
/// Metadata read from a .torrent file.
/// </summary>
/// <param name="InfoHash">BitTorrent v1 info hash, lowercase hex.</param>
/// <param name="Name">Torrent name (root folder or single file name).</param>
/// <param name="Files">Files in torrent order.</param>
public sealed record TorrentMeta(string InfoHash, string Name, IReadOnlyList<TorrentFileEntry> Files)
{
    /// <summary>
    /// Parses a .torrent file.
    /// </summary>
    /// <param name="data">File bytes.</param>
    /// <returns>Metadata, or <c>null</c> when the data is not a valid torrent.</returns>
    public static TorrentMeta? TryParse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0 || data[0] != (byte)'d')
        {
            return null; // HTML error page, not a torrent
        }

        try
        {
            if (Bencode.Parse(data) is not BencodeDictionary root
                || root.GetDictionary("info") is not { } info
                || !root.TryGetRange("info", out var range))
            {
                return null;
            }

#pragma warning disable CA5350 // BitTorrent v1 defines the info hash as SHA-1; not used for security.
            var hash = Convert.ToHexString(SHA1.HashData(data.AsSpan(range.Start, range.End - range.Start))).ToLowerInvariant();
#pragma warning restore CA5350
            var name = info.GetString("name.utf-8") ?? info.GetString("name") ?? hash;

            var files = new List<TorrentFileEntry>();
            if (info.GetList("files") is { } list)
            {
                foreach (var item in list.OfType<BencodeDictionary>())
                {
                    var parts = (item.GetList("path.utf-8") ?? item.GetList("path") ?? [])
                        .OfType<byte[]>()
                        .Select(p => System.Text.Encoding.UTF8.GetString(p))
                        .ToList();
                    if (parts.Count == 0)
                    {
                        continue;
                    }

                    files.Add(new TorrentFileEntry(files.Count, string.Join('/', parts), item.GetInteger("length") ?? 0));
                }
            }
            else
            {
                files.Add(new TorrentFileEntry(0, name, info.GetInteger("length") ?? 0));
            }

            return files.Count == 0 ? null : new TorrentMeta(hash, name, files);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
