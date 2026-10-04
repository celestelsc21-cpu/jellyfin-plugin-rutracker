using System;

namespace Jellyfin.Plugin.RuTracker.Downloads;

/// <summary>
/// A download request that cannot be fulfilled; the message is safe to show to the user.
/// </summary>
public class DownloadException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadException"/> class.
    /// </summary>
    public DownloadException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadException"/> class.
    /// </summary>
    /// <param name="message">User-facing message.</param>
    public DownloadException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadException"/> class.
    /// </summary>
    /// <param name="message">User-facing message.</param>
    /// <param name="innerException">Underlying error.</param>
    public DownloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
