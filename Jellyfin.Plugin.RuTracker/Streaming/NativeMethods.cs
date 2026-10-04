using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.RuTracker.Streaming;

/// <summary>
/// Minimal libc calls used to read a file without taking a lock on it.
/// </summary>
#pragma warning disable SYSLIB1054 // LibraryImport would require unsafe code in the plugin.
internal static class NativeMethods
{
    /// <summary>
    /// open(2) read-only flag.
    /// </summary>
    public const int ReadOnly = 0;

    /// <summary>
    /// Opens a file.
    /// </summary>
    /// <param name="path">Path (UTF-8).</param>
    /// <param name="flags">Open flags.</param>
    /// <returns>File descriptor or -1.</returns>
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    /// <summary>
    /// Reads at an offset (64-bit offsets on every architecture).
    /// </summary>
    /// <param name="fd">File descriptor.</param>
    /// <param name="buffer">Destination; filled from index 0.</param>
    /// <param name="count">Bytes to read.</param>
    /// <param name="offset">File offset.</param>
    /// <returns>Bytes read, 0 at end of file, -1 on error.</returns>
    [DllImport("libc", EntryPoint = "pread64", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern nint PRead(int fd, byte[] buffer, nuint count, long offset);

    /// <summary>
    /// Closes a file descriptor.
    /// </summary>
    /// <param name="fd">File descriptor.</param>
    /// <returns>0 on success.</returns>
    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern int Close(int fd);
}
#pragma warning restore SYSLIB1054
