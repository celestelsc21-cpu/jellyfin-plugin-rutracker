using System;

namespace Jellyfin.Plugin.RuTracker.Tracker;

/// <summary>
/// RuTracker communication failure. <see cref="Exception.Message"/> is safe to
/// show to the user (Russian, no secrets).
/// </summary>
public class RuTrackerException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RuTrackerException"/> class.
    /// </summary>
    public RuTrackerException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RuTrackerException"/> class.
    /// </summary>
    /// <param name="message">User-facing message.</param>
    public RuTrackerException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RuTrackerException"/> class.
    /// </summary>
    /// <param name="message">User-facing message.</param>
    /// <param name="innerException">Underlying error.</param>
    public RuTrackerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
