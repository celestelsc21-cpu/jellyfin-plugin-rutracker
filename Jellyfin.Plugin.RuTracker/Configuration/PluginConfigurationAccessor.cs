using System;
using MediaBrowser.Common.Plugins;

namespace Jellyfin.Plugin.RuTracker.Configuration;

/// <summary>
/// Resolves the plugin instance through <see cref="IPluginManager"/>.
/// </summary>
internal sealed class PluginConfigurationAccessor : IPluginConfigurationAccessor
{
    private static readonly Guid PluginId = Guid.Parse(Plugin.PluginGuid);
    private readonly IPluginManager _pluginManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfigurationAccessor"/> class.
    /// </summary>
    /// <param name="pluginManager">Plugin manager.</param>
    public PluginConfigurationAccessor(IPluginManager pluginManager)
    {
        _pluginManager = pluginManager;
    }

    /// <inheritdoc />
    public PluginConfiguration Current
        => (_pluginManager.GetPlugin(PluginId)?.Instance as Plugin)?.Configuration
           ?? throw new InvalidOperationException("RuTracker plugin instance is not loaded.");
}
