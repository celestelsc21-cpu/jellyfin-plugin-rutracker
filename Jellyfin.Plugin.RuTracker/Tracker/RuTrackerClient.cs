using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Jellyfin.Plugin.RuTracker.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RuTracker.Tracker;

/// <summary>
/// RuTracker client. All site requests are serialized and spaced out to respect
/// the site; the login session is kept in memory and renewed on expiry.
/// </summary>
internal sealed class RuTrackerClient : IRuTrackerClient, IDisposable
{
    /// <summary>
    /// Named <see cref="HttpClient"/> registered for RuTracker.
    /// </summary>
    public const string HttpClientName = "RuTracker";

    private static readonly Encoding Cp1251 = CreateCp1251();
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SearchCacheTtl = TimeSpan.FromMinutes(10);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPluginConfigurationAccessor _config;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RuTrackerClient> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Guarded by _gate.
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private string? _session;
    private string? _sessionSettingsKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="RuTrackerClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="config">Configuration accessor.</param>
    /// <param name="cache">Memory cache.</param>
    /// <param name="logger">Logger.</param>
    public RuTrackerClient(
        IHttpClientFactory httpClientFactory,
        IPluginConfigurationAccessor config,
        IMemoryCache cache,
        ILogger<RuTrackerClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TorrentInfo>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var normalized = SearchQuery.Normalize(query)
            ?? throw new ArgumentException("Query is too short.", nameof(query));

        var cacheKey = "rutracker:search:" + normalized.ToUpperInvariant();
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<TorrentInfo>? cached) && cached is not null)
        {
            return cached;
        }

        // o=10: order by seeders, s=2: descending. RuTracker expects windows-1251 in the query string.
        var path = "forum/tracker.php?nm=" + HttpUtility.UrlEncode(normalized, Cp1251) + "&o=10&s=2";
        var html = await GetAuthenticatedPageAsync(path, cancellationToken).ConfigureAwait(false);
        var results = TrackerHtmlParser.ParseSearchResults(html);

        _logger.LogDebug("RuTracker search returned {Count} rows", results.Count);
        _cache.Set(cacheKey, results, SearchCacheTtl);
        return results;
    }

    /// <inheritdoc />
    public async Task CheckLoginAsync(CancellationToken cancellationToken)
    {
        await GetAuthenticatedPageAsync("forum/index.php", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private static Encoding CreateCp1251()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1251);
    }

    private static string SettingsKey(PluginConfiguration config)
    {
        var raw = string.Join('\n', config.RuTrackerBaseUrl, config.RuTrackerUsername, config.RuTrackerPassword, config.RuTrackerSessionCookie);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static Uri BuildUri(PluginConfiguration config, string relative)
    {
        if (!ConfigurationValidator.IsAllowedRuTrackerUrl(config.RuTrackerBaseUrl))
        {
            throw new RuTrackerException("Адрес RuTracker в настройках плагина некорректен.");
        }

        return new Uri(new Uri(config.RuTrackerBaseUrl.TrimEnd('/') + "/"), relative);
    }

    private async Task<string> GetAuthenticatedPageAsync(string relative, CancellationToken cancellationToken)
    {
        var config = _config.Current;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settingsKey = SettingsKey(config);
            if (!string.Equals(settingsKey, _sessionSettingsKey, StringComparison.Ordinal))
            {
                // Settings changed: forget the old session.
                _session = SessionCookie.Normalize(config.RuTrackerSessionCookie);
                _sessionSettingsKey = settingsKey;
            }

            if (_session is null)
            {
                _session = await LoginAsync(config, cancellationToken).ConfigureAwait(false);
            }

            var html = await GetPageAsync(config, relative, _session, cancellationToken).ConfigureAwait(false);
            if (TrackerHtmlParser.IsLoggedIn(html))
            {
                return html;
            }

            // Session expired: log in again once.
            _logger.LogInformation("RuTracker session expired, logging in again");
            _session = await LoginAsync(config, cancellationToken).ConfigureAwait(false);
            html = await GetPageAsync(config, relative, _session, cancellationToken).ConfigureAwait(false);
            if (!TrackerHtmlParser.IsLoggedIn(html))
            {
                _session = null;
                throw new RuTrackerException("RuTracker не принял вход. Проверьте логин и пароль в настройках плагина.");
            }

            return html;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> LoginAsync(PluginConfiguration config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.RuTrackerUsername) || string.IsNullOrEmpty(config.RuTrackerPassword))
        {
            throw new RuTrackerException(string.IsNullOrWhiteSpace(config.RuTrackerSessionCookie)
                ? "В настройках плагина не задан логин и пароль RuTracker."
                : "Cookie bb_session устарела, а логин и пароль RuTracker не заданы. Обновите cookie в настройках плагина.");
        }

        var body = "login_username=" + HttpUtility.UrlEncode(config.RuTrackerUsername, Cp1251)
            + "&login_password=" + HttpUtility.UrlEncode(config.RuTrackerPassword, Cp1251)
            + "&login=" + HttpUtility.UrlEncode("вход", Cp1251);
        var uri = BuildUri(config, "forum/login.php");

        using var response = await SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, uri)
                {
                    Content = new ByteArrayContent(Encoding.ASCII.GetBytes(body))
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
                return request;
            },
            cancellationToken).ConfigureAwait(false);

        var session = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? SessionCookie.FromSetCookie(cookies)
            : null;
        if (session is not null)
        {
            _logger.LogInformation("Logged in to RuTracker");
            return session;
        }

        var html = Cp1251.GetString(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
        if (TrackerHtmlParser.HasCaptcha(html))
        {
            _logger.LogWarning("RuTracker login requires a captcha");
            throw new RuTrackerException(
                "RuTracker требует ввести капчу. Войдите на сайт в браузере и вставьте значение cookie bb_session в настройках плагина.");
        }

        _logger.LogWarning("RuTracker login rejected, status {Status}", (int)response.StatusCode);
        throw new RuTrackerException("RuTracker не принял вход. Проверьте логин и пароль в настройках плагина.");
    }

    private async Task<string> GetPageAsync(PluginConfiguration config, string relative, string session, CancellationToken cancellationToken)
    {
        var uri = BuildUri(config, relative);
        using var response = await SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Add("Cookie", SessionCookie.Name + "=" + session);
                return request;
            },
            cancellationToken).ConfigureAwait(false);

        // Redirects are not followed (to keep cookies under control); a redirect
        // from a content page means "go to login", which callers detect as logged out.
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return Cp1251.GetString(bytes);
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        const int Attempts = 2;
        var attempt = 0;
        while (true)
        {
            attempt++;
            var wait = _lastRequest + MinRequestInterval - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            _lastRequest = DateTimeOffset.UtcNow;
            using var request = requestFactory();
            try
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode < 500 || attempt == Attempts)
                {
                    if ((int)response.StatusCode >= 500)
                    {
                        response.Dispose();
                        throw new RuTrackerException("RuTracker временно недоступен. Попробуйте позже.");
                    }

                    return response;
                }

                response.Dispose();
                _logger.LogWarning("RuTracker answered {Status}, retrying", (int)response.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                if (attempt == Attempts)
                {
                    _logger.LogWarning(ex, "RuTracker is unreachable");
                    throw new RuTrackerException("Не удаётся связаться с RuTracker. Проверьте доступ сервера к сайту.", ex);
                }

                _logger.LogWarning("RuTracker request failed ({Error}), retrying", ex.GetType().Name);
            }

            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
        }
    }
}
