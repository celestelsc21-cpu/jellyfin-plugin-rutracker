using System;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.RuTracker.Access;

/// <summary>
/// Requires a plugin role for an action. Apply on actions (not controllers) so
/// that the controller-level <c>[Authorize]</c> filter runs first.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireRuTrackerRoleAttribute : TypeFilterAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequireRuTrackerRoleAttribute"/> class.
    /// </summary>
    /// <param name="role">Required role.</param>
    public RequireRuTrackerRoleAttribute(AccessRole role)
        : base(typeof(RuTrackerAccessFilter))
    {
        Role = role;
        Arguments = [role];
    }

    /// <summary>
    /// Gets the required role.
    /// </summary>
    public AccessRole Role { get; }
}
