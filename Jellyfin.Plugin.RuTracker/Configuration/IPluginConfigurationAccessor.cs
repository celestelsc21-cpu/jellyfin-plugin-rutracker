namespace Jellyfin.Plugin.RuTracker.Configuration;

/// <summary>
/// DI-friendly access to the current plugin configuration
/// (replaces the static <c>Plugin.Instance</c> pattern).
/// </summary>
public interface IPluginConfigurationAccessor
{
    /// <summary>
    /// Gets the current configuration snapshot. Never cache it across requests:
    /// the administrator may change settings at any time.
    /// </summary>
    PluginConfiguration Current { get; }
}
