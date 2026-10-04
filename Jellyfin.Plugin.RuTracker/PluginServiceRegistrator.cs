using System;
using System.Net;
using System.Net.Http;
using Jellyfin.Plugin.RuTracker.Access;
using Jellyfin.Plugin.RuTracker.Configuration;
using Jellyfin.Plugin.RuTracker.QBittorrent;
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

                // A regular browser identity: some sites answer non-browser clients differently.
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru-RU,ru;q=0.9,en;q=0.5");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Cookies and redirects are handled explicitly by RuTrackerClient.
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.All
            });
        serviceCollection.AddSingleton<IRuTrackerClient, RuTrackerClient>();

        serviceCollection
            .AddHttpClient(QBittorrentClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The Web UI session cookie is handled explicitly by QBittorrentClient.
                AllowAutoRedirect = false,
                UseCookies = false
            });
        serviceCollection.AddSingleton<IQBittorrentClient, QBittorrentClient>();

        // Adds the RuTracker button to the web client header.
        serviceCollection.AddTransient<IStartupFilter, WebInjectionStartupFilter>();
    }
}
