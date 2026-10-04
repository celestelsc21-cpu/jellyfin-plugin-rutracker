using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RuTracker.Configuration;
using Jellyfin.Plugin.RuTracker.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RuTracker.QBittorrent;

/// <summary>
/// qBittorrent Web API v2 client (connection check; download control follows in later versions).
/// </summary>
internal sealed class QBittorrentClient : IQBittorrentClient
{
    /// <summary>
    /// Named <see cref="HttpClient"/> registered for qBittorrent.
    /// </summary>
    public const string HttpClientName = "QBittorrent";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPluginConfigurationAccessor _config;
    private readonly ILogger<QBittorrentClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="QBittorrentClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="config">Configuration accessor.</param>
    /// <param name="logger">Logger.</param>
    public QBittorrentClient(IHttpClientFactory httpClientFactory, IPluginConfigurationAccessor config, ILogger<QBittorrentClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DiagnosticReport> DiagnoseAsync(CancellationToken cancellationToken)
    {
        var steps = new List<DiagnosticStep>();
        try
        {
            return await DiagnoseCoreAsync(_config.Current, steps, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "qBittorrent connection check failed");
            steps.Add(new DiagnosticStep("Соединение", false, "Связь с qBittorrent прервалась во время проверки. Повторите попытку."));
            return new DiagnosticReport(false, steps);
        }
    }

    private async Task<DiagnosticReport> DiagnoseCoreAsync(PluginConfiguration config, List<DiagnosticStep> steps, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(config.QBittorrentUrl?.Trim(), UriKind.Absolute, out var configured)
            || (configured.Scheme != Uri.UriSchemeHttp && configured.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(configured.UserInfo))
        {
            steps.Add(new DiagnosticStep("Адрес", false, "Не задан корректный адрес Web UI qBittorrent, например http://192.168.1.20:8080."));
            return new DiagnosticReport(false, steps);
        }

        var baseUri = new Uri(configured.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
        var endpoint = baseUri.Authority;

        // 1. Reachability. 200 means authentication is bypassed for the server's address.
        Response version;
        try
        {
            version = await GetAsync(baseUri, "api/v2/app/version", null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "qBittorrent at {Endpoint} is unreachable", endpoint);
            steps.Add(new DiagnosticStep(
                "Соединение",
                false,
                $"qBittorrent не отвечает по адресу {endpoint}. Проверьте, что qBittorrent запущен, Web UI включён (Настройки → Веб-интерфейс), а брандмауэр Windows пропускает этот порт."));
            return new DiagnosticReport(false, steps);
        }

        string? cookie = null;
        if (version.Status == HttpStatusCode.OK)
        {
            steps.Add(new DiagnosticStep("Соединение", true, "Web UI отвечает; вход не требуется (адрес сервера в белом списке qBittorrent)."));
        }
        else if (version.Status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            steps.Add(new DiagnosticStep("Соединение", true, "Web UI отвечает."));

            // 2. Login.
            if (string.IsNullOrWhiteSpace(config.QBittorrentUsername))
            {
                steps.Add(new DiagnosticStep("Вход", false, "qBittorrent требует логин и пароль, а они не заданы в настройках плагина."));
                return new DiagnosticReport(false, steps);
            }

            var login = await LoginAsync(baseUri, config, cancellationToken).ConfigureAwait(false);
            if (login.Status == HttpStatusCode.Forbidden)
            {
                steps.Add(new DiagnosticStep(
                    "Вход",
                    false,
                    "qBittorrent временно заблокировал адрес сервера после неудачных попыток входа. Подождите или перезапустите qBittorrent, затем проверьте логин и пароль."));
                return new DiagnosticReport(false, steps);
            }

            if (!QBittorrentProtocol.IsLoginAccepted(login.Body) || login.Cookie is null)
            {
                steps.Add(new DiagnosticStep("Вход", false, "qBittorrent отклонил логин или пароль."));
                return new DiagnosticReport(false, steps);
            }

            cookie = login.Cookie;
            steps.Add(new DiagnosticStep("Вход", true, "Логин и пароль приняты."));
            version = await GetAsync(baseUri, "api/v2/app/version", cookie, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            steps.Add(new DiagnosticStep(
                "Соединение",
                false,
                "По адресу " + endpoint + " отвечает не qBittorrent (код " + ((int)version.Status).ToString(CultureInfo.InvariantCulture) + ")."));
            return new DiagnosticReport(false, steps);
        }

        // 3. Version.
        var api = await GetAsync(baseUri, "api/v2/app/webapiVersion", cookie, cancellationToken).ConfigureAwait(false);
        if (version.Status != HttpStatusCode.OK)
        {
            steps.Add(new DiagnosticStep("Версия", false, "Не удалось получить версию qBittorrent."));
            return new DiagnosticReport(false, steps);
        }

        steps.Add(new DiagnosticStep("Версия", true, $"qBittorrent {version.Body.Trim()}, Web API {api.Body.Trim()}."));

        // 4. Default folder and how it maps to Jellyfin (informational).
        var preferences = await GetAsync(baseUri, "api/v2/app/preferences", cookie, cancellationToken).ConfigureAwait(false);
        var savePath = preferences.Status == HttpStatusCode.OK ? QBittorrentProtocol.ReadSavePath(preferences.Body) : null;
        if (savePath is not null)
        {
            var mapper = new PathMapper(config.PathMappings ?? []);
            steps.Add(new DiagnosticStep(
                "Папка по умолчанию",
                true,
                mapper.TryToJellyfin(savePath, out var local)
                    ? $"{savePath} → в Jellyfin это {local}."
                    : $"{savePath} (не входит в соответствия путей — это не ошибка: плагин всегда указывает папку загрузки явно)."));
        }

        return new DiagnosticReport(true, steps);
    }

    private async Task<Response> LoginAsync(Uri baseUri, PluginConfiguration config, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "api/v2/auth/login"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = config.QBittorrentUsername,
                ["password"] = config.QBittorrentPassword
            })
        };

        // qBittorrent's CSRF protection requires Referer/Origin matching its own address.
        request.Headers.Referrer = baseUri;
        request.Headers.Add("Origin", baseUri.GetLeftPart(UriPartial.Authority));

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var cookie = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? QBittorrentProtocol.ExtractSessionCookie(cookies)
            : null;
        return new Response(response.StatusCode, body, cookie);
    }

    private async Task<Response> GetAsync(Uri baseUri, string relative, string? cookie, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relative));
        request.Headers.Referrer = baseUri;
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new Response(response.StatusCode, body, null);
    }

    private sealed record Response(HttpStatusCode Status, string Body, string? Cookie);
}
