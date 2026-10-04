using System;
using System.Linq;
using System.Security.Claims;

namespace Jellyfin.Plugin.RuTracker.Access;

/// <summary>
/// Reads the caller identity from claims issued by Jellyfin's authentication handler.
/// </summary>
/// <remarks>
/// Claim names mirror <c>Jellyfin.Api.Constants.InternalClaimTypes</c> (10.11.x).
/// That assembly is not published on NuGet, so the names are duplicated here;
/// re-check them when moving to a new server version.
/// </remarks>
internal static class CallerIdentity
{
    /// <summary>
    /// Claim carrying the Jellyfin user id.
    /// </summary>
    public const string UserIdClaim = "Jellyfin-UserId";

    /// <summary>
    /// Claim set to "True" for requests authenticated by an API key.
    /// </summary>
    public const string IsApiKeyClaim = "Jellyfin-IsApiKey";

    /// <summary>
    /// Gets the user id, or <see cref="Guid.Empty"/> when absent or malformed.
    /// </summary>
    /// <param name="principal">Caller principal.</param>
    /// <returns>User id.</returns>
    public static Guid GetUserId(ClaimsPrincipal principal)
        => Guid.TryParse(GetClaim(principal, UserIdClaim), out var id) ? id : Guid.Empty;

    /// <summary>
    /// Gets a value indicating whether the caller uses an API key (unrestricted in Jellyfin).
    /// </summary>
    /// <param name="principal">Caller principal.</param>
    /// <returns><c>true</c> for API key requests.</returns>
    public static bool IsApiKey(ClaimsPrincipal principal)
        => bool.TryParse(GetClaim(principal, IsApiKeyClaim), out var value) && value;

    private static string? GetClaim(ClaimsPrincipal principal, string type)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.Claims.FirstOrDefault(c => c.Type.Equals(type, StringComparison.OrdinalIgnoreCase))?.Value;
    }
}
