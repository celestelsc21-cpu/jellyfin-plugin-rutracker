namespace Jellyfin.Plugin.RuTracker.Api.Models;

/// <summary>
/// A user-facing message (status or error).
/// </summary>
/// <param name="Message">Message safe to show to the user.</param>
public sealed record MessageDto(string Message);
