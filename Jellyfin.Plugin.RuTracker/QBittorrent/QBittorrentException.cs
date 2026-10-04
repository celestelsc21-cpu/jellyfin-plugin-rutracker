using System;

namespace Jellyfin.Plugin.RuTracker.QBittorrent;

/// <summary>
/// qBittorrent request failure. <see cref="Exception.Message"/> is safe to show to the user.
/// </summary>
public class QBittorrentException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QBittorrentException"/> class.
    /// </summary>
    public QBittorrentException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QBittorrentException"/> class.
    /// </summary>
    /// <param name="message">User-facing message.</param>
    public QBittorrentException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QBittorrentException"/> class.
    /// </summary>
    /// <param name="message">User-facing message.</param>
    /// <param name="innerException">Underlying error.</param>
    public QBittorrentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
