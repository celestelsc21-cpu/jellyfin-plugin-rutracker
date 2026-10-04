using System;

namespace Jellyfin.Plugin.RuTracker.Tracker;

/// <summary>
/// The RuTracker host could not be reached or returned something that is not
/// RuTracker (for example an ISP block page). Another mirror may still work.
/// </summary>
public class RuTrackerUnavailableException : RuTrackerException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RuTrackerUnavailableException"/> class.
    /// </summary>
    public RuTrackerUnavailableException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RuTrackerUnavailableException"/> class.
    /// </summary>
    /// <param name="message">User-facing message.</param>
    public RuTrackerUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RuTrackerUnavailableException"/> class.
    /// </summary>
    /// <param name="message">User-facing message.</param>
    /// <param name="innerException">Underlying error.</param>
    public RuTrackerUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
