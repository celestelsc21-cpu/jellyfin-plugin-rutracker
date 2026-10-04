using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.RuTracker.Streaming;

/// <summary>
/// Reads a file that another program is still writing, without locking it.
/// </summary>
/// <remarks>
/// On Linux, <see cref="FileStream"/> takes an advisory <c>flock</c>. On an SMB/CIFS mount the
/// kernel turns it into a Windows byte-range lock, and qBittorrent on Windows then fails with
/// "the process cannot access the file" and stops the torrent. Plain <c>open</c>/<c>pread</c>
/// take no lock. Elsewhere (or if libc is unusable) a shared <see cref="FileStream"/> is used.
/// </remarks>
internal sealed class UnlockedFileReader : IDisposable
{
    private readonly int _fd = -1;
    private readonly FileStream? _stream;

    private UnlockedFileReader(int fd)
    {
        _fd = fd;
    }

    private UnlockedFileReader(FileStream stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// Opens a file for reading.
    /// </summary>
    /// <param name="path">Path.</param>
    /// <returns>The reader.</returns>
    public static UnlockedFileReader Open(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var fd = NativeMethods.Open(path, NativeMethods.ReadOnly);
                if (fd < 0)
                {
                    throw new IOException("Cannot open the file (errno " + Marshal.GetLastPInvokeError().ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");
                }

                return new UnlockedFileReader(fd);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // Fall through to FileStream.
            }
        }

        return new UnlockedFileReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None));
    }

    /// <summary>
    /// Reads bytes at a position into the start of <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">Destination.</param>
    /// <param name="count">Maximum bytes.</param>
    /// <param name="position">File offset.</param>
    /// <returns>Bytes read; 0 at the current end of the file.</returns>
    public int Read(byte[] buffer, int count, long position)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, buffer.Length);
        if (_stream is not null)
        {
            _stream.Position = position;
            return _stream.Read(buffer, 0, count);
        }

        var read = NativeMethods.PRead(_fd, buffer, (nuint)count, position);
        if (read < 0)
        {
            throw new IOException("Cannot read the file (errno " + Marshal.GetLastPInvokeError().ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");
        }

        return (int)read;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_stream is not null)
        {
            _stream.Dispose();
        }
        else if (_fd >= 0)
        {
            _ = NativeMethods.Close(_fd);
        }
    }
}
