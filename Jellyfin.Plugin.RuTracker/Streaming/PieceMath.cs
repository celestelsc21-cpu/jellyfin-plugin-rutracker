using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.RuTracker.Streaming;

/// <summary>
/// Maps byte ranges of a file to torrent pieces. Pure logic.
/// </summary>
/// <remarks>
/// qBittorrent reports for each file the range of pieces it touches, not its exact byte
/// offset. A file starts somewhere inside its first piece, so byte <c>p</c> of the file lies
/// in piece <c>first + floor(p / size)</c> or the next one. Requiring both is a safe
/// over-approximation of at most one extra piece.
/// </remarks>
internal static class PieceMath
{
    /// <summary>
    /// qBittorrent piece state: downloaded.
    /// </summary>
    public const int Downloaded = 2;

    /// <summary>
    /// Gets the pieces that must be present to read a byte range of a file.
    /// </summary>
    /// <param name="fileFirstPiece">First piece of the file.</param>
    /// <param name="fileLastPiece">Last piece of the file.</param>
    /// <param name="pieceSize">Piece size in bytes.</param>
    /// <param name="position">Start position inside the file.</param>
    /// <param name="length">Number of bytes to read (at least 1).</param>
    /// <returns>Inclusive piece range.</returns>
    public static (int First, int Last) RequiredPieces(int fileFirstPiece, int fileLastPiece, long pieceSize, long position, long length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pieceSize);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        var first = fileFirstPiece + (position / pieceSize);
        var last = fileFirstPiece + ((position + length - 1) / pieceSize) + 1;
        return ((int)Math.Clamp(first, fileFirstPiece, fileLastPiece), (int)Math.Clamp(last, fileFirstPiece, fileLastPiece));
    }

    /// <summary>
    /// Gets a value indicating whether every piece of the range is downloaded.
    /// </summary>
    /// <param name="states">Piece states from qBittorrent.</param>
    /// <param name="first">First piece.</param>
    /// <param name="last">Last piece.</param>
    /// <returns><c>true</c> when the data can be read.</returns>
    public static bool IsAvailable(IReadOnlyList<int> states, int first, int last)
    {
        ArgumentNullException.ThrowIfNull(states);
        if (first < 0 || last >= states.Count || first > last)
        {
            return false;
        }

        for (var i = first; i <= last; i++)
        {
            if (states[i] != Downloaded)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets how many bytes, starting at <paramref name="position"/>, are certainly readable.
    /// </summary>
    /// <param name="states">Piece states.</param>
    /// <param name="fileFirstPiece">First piece of the file.</param>
    /// <param name="fileLastPiece">Last piece of the file.</param>
    /// <param name="pieceSize">Piece size.</param>
    /// <param name="position">Start position inside the file.</param>
    /// <param name="fileSize">File size.</param>
    /// <returns>Readable byte count (0 when the next byte is not downloaded yet).</returns>
    public static long ReadableFrom(IReadOnlyList<int> states, int fileFirstPiece, int fileLastPiece, long pieceSize, long position, long fileSize)
    {
        ArgumentNullException.ThrowIfNull(states);
        if (position >= fileSize)
        {
            return 0;
        }

        // Grow piece by piece while every needed piece (with the one-piece safety margin) is present.
        long readable = 0;
        while (position + readable < fileSize)
        {
            var step = Math.Min(pieceSize, fileSize - position - readable);
            var (first, last) = RequiredPieces(fileFirstPiece, fileLastPiece, pieceSize, position + readable, step);
            if (!IsAvailable(states, first, last))
            {
                break;
            }

            readable += step;
        }

        return readable;
    }
}
