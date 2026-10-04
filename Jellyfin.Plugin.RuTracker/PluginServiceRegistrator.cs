using System;
using System.Net;
using System.Net.Http;
using Jellyfin.Plugin.RuTracker.Access;
using Jellyfin.Plugin.RuTracker.Configuration;
using Jellyfin.Plugin.RuTracker.Tracker;
using Jellyfin.Plugin.RuTracker.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.RuTracker;

/// <summary>
/// Registers plugin services with the server's DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IPluginConfigurationAccessor, PluginConfigurationAccessor>();
        serviceCollection.AddSingleton<IAccessService, AccessService>();

        serviceCollection
            .AddHttpClient(RuTrackerClient.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(20);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Jellyfin-RuTracker-Plugin)");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Cookies and redirects are handled explicitly by RuTrackerClient.
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.All
            });
        serviceCollection.AddSingleton<IRuTrackerClient, RuTrackerClient>();

        // Adds the RuTracker button to the web client header.
        serviceCollection.AddTransient<IStartupFilter, WebInjectionStartupFilter>();
    }
}
