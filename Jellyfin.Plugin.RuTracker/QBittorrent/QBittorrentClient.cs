using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RuTracker.Configuration;
using Jellyfin.Plugin.RuTracker.Diagnostics;
using Jellyfin.Plugin.RuTracker.Tracker;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RuTracker.QBittorrent;

/// <summary>
/// qBittorrent Web API v2 client. The Web UI session is cached and renewed
/// automatically when qBittorrent drops it (HTTP 403).
/// </summary>
internal sealed class QBittorrentClient : IQBittorrentClient, IDisposable
{
    /// <summary>
    /// Named <see cref="HttpClient"/> registered for qBittorrent.
    /// </summary>
    public const string HttpClientName = "QBittorrent";

    private const string NotFoundKey = "qbt-not-found";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPluginConfigurationAccessor _config;
    private readonly ILogger<QBittorrentClient> _logger;
    private readonly SemaphoreSlim _loginGate = new(1, 1);

    // Session cookie and the settings it belongs to; guarded by _loginGate for writes.
    private volatile SessionState? _session;

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

    /// <inheritdoc />
    public async Task AddTorrentAsync(TopicTorrent torrent, string savePath, string category, bool stopped, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        var hash = torrent.InfoHash ?? throw new QBittorrentException("У раздачи нет ни торрент-файла, ни magnet-ссылки.");

        await EnsureCategoryAsync(category, cancellationToken).ConfigureAwait(false);

        var body = await SendAsync(
            baseUri =>
            {
                var form = new MultipartFormDataContent();
                if (!torrent.TorrentFile.IsEmpty)
                {
                    var file = new ByteArrayContent(torrent.TorrentFile.ToArray());
                    file.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
                    form.Add(file, "torrents", torrent.TopicId.ToString(CultureInfo.InvariantCulture) + ".torrent");
                }
                else
                {
                    form.Add(new StringContent(torrent.Magnet!), "urls");
                }

                form.Add(new StringContent(savePath), "savepath");
                form.Add(new StringContent(category), "category");
                form.Add(new StringContent("false"), "autoTMM");
                form.Add(new StringContent("true"), "sequentialDownload");
                form.Add(new StringContent("true"), "firstLastPiecePrio");

                // "stopped" is the qBittorrent 5 name, "paused" the 4.x one; unknown fields are ignored.
                form.Add(new StringContent(stopped ? "true" : "false"), "stopped");
                form.Add(new StringContent(stopped ? "true" : "false"), "paused");
                return new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "api/v2/torrents/add")) { Content = form };
            },
            cancellationToken).ConfigureAwait(false);

        if (QBittorrentProtocol.IsLoginAccepted(body))
        {
            _logger.LogInformation("Added torrent {Hash} to qBittorrent", hash);
            return;
        }

        // "Fails." is also returned when the torrent is already there.
        var existing = await GetTorrentsAsync([hash], cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            _logger.LogInformation("Torrent {Hash} is already in qBittorrent", hash);
            return;
        }

        throw new QBittorrentException("qBittorrent не принял раздачу. Проверьте, что папка загрузки существует на компьютере с qBittorrent.");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QbTorrent>> GetTorrentsAsync(IReadOnlyCollection<string> hashes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        if (hashes.Count == 0)
        {
            return [];
        }

        var query = "api/v2/torrents/info?hashes=" + Uri.EscapeDataString(string.Join('|', hashes));
        var json = await SendAsync(baseUri => new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, query)), cancellationToken).ConfigureAwait(false);
        return Deserialize<List<QbTorrent>>(json) ?? [];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QbFile>> GetFilesAsync(string hash, CancellationToken cancellationToken)
    {
        var query = "api/v2/torrents/files?hash=" + Uri.EscapeDataString(hash);
        var json = await SendAsync(baseUri => new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, query)), cancellationToken).ConfigureAwait(false);
        var files = Deserialize<List<QbFile>>(json) ?? [];

        // Older Web API versions omit "index"; it equals the position in the list.
        for (var i = 0; i < files.Count; i++)
        {
            if (files[i].Index == 0 && i > 0)
            {
                files[i].Index = i;
            }
        }

        return files;
    }

    /// <inheritdoc />
    public async Task SetFilePriorityAsync(string hash, IReadOnlyCollection<int> fileIndexes, int priority, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileIndexes);
        if (fileIndexes.Count == 0)
        {
            return;
        }

        await PostFormAsync(
            "api/v2/torrents/filePrio",
            new Dictionary<string, string>
            {
                ["hash"] = hash,
                ["id"] = string.Join('|', fileIndexes.Select(i => i.ToString(CultureInfo.InvariantCulture))),
                ["priority"] = priority.ToString(CultureInfo.InvariantCulture)
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StartAsync(string hash, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string> { ["hashes"] = hash };
        try
        {
            await PostFormAsync("api/v2/torrents/start", form, cancellationToken).ConfigureAwait(false);
        }
        catch (QBittorrentException ex) when (ex.Data.Contains(NotFoundKey))
        {
            // qBittorrent 4.x calls it "resume".
            await PostFormAsync("api/v2/torrents/resume", form, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(string hash, bool deleteFiles, CancellationToken cancellationToken)
        => PostFormAsync(
            "api/v2/torrents/delete",
            new Dictionary<string, string> { ["hashes"] = hash, ["deleteFiles"] = deleteFiles ? "true" : "false" },
            cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _loginGate.Dispose();

    private static T? Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException ex)
        {
            throw new QBittorrentException("qBittorrent вернул непонятный ответ.", ex);
        }
    }

    private static Uri? ResolveBase(PluginConfiguration config)
    {
        if (!Uri.TryCreate(config.QBittorrentUrl?.Trim(), UriKind.Absolute, out var configured)
            || (configured.Scheme != Uri.UriSchemeHttp && configured.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(configured.UserInfo))
        {
            return null;
        }

        return new Uri(configured.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
    }

    private static string SettingsKey(PluginConfiguration config)
        => string.Join('\n', config.QBittorrentUrl, config.QBittorrentUsername, config.QBittorrentPassword);

    private async Task EnsureCategoryAsync(string category, CancellationToken cancellationToken)
    {
        try
        {
            await PostFormAsync(
                "api/v2/torrents/createCategory",
                new Dictionary<string, string> { ["category"] = category, ["savePath"] = string.Empty },
                cancellationToken).ConfigureAwait(false);
        }
        catch (QBittorrentException)
        {
            // 409: the category already exists — that is fine.
        }
    }

    private async Task PostFormAsync(string relative, Dictionary<string, string> fields, CancellationToken cancellationToken)
        => await SendAsync(
            baseUri => new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, relative)) { Content = new FormUrlEncodedContent(fields) },
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Sends an authorized request and returns the body. Logs in when needed and retries once on 403.
    /// </summary>
    private async Task<string> SendAsync(Func<Uri, HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        var config = _config.Current;
        var baseUri = ResolveBase(config) ?? throw new QBittorrentException("В настройках плагина не задан адрес qBittorrent.");

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var session = await GetSessionAsync(config, baseUri, forceLogin: attempt > 1, cancellationToken).ConfigureAwait(false);
            using var request = requestFactory(baseUri);
            request.Headers.Referrer = baseUri;
            if (session.Cookie is not null)
            {
                request.Headers.Add("Cookie", session.Cookie);
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                throw new QBittorrentException("qBittorrent не отвечает. Проверьте, что он запущен и доступен по сети.", ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Forbidden && attempt == 1)
                {
                    continue; // session expired: log in again
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return body;
                }

                var error = new QBittorrentException(
                    "qBittorrent ответил ошибкой " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
                    + (string.IsNullOrWhiteSpace(body) ? "." : ": " + body.Trim()));
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    error.Data[NotFoundKey] = true;
                }

                throw error;
            }
        }

        throw new QBittorrentException("qBittorrent отклоняет запросы плагина. Нажмите «Проверить подключение» в настройках.");
    }

    private async Task<SessionState> GetSessionAsync(PluginConfiguration config, Uri baseUri, bool forceLogin, CancellationToken cancellationToken)
    {
        var key = SettingsKey(config);
        var current = _session;
        if (!forceLogin && current is not null && current.SettingsKey == key)
        {
            return current;
        }

        await _loginGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            current = _session;
            if (!forceLogin && current is not null && current.SettingsKey == key)
            {
                return current;
            }

            SessionState state;
            if (string.IsNullOrWhiteSpace(config.QBittorrentUsername))
            {
                state = new SessionState(key, null); // authentication bypass for the server's address
            }
            else
            {
                var login = await LoginAsync(baseUri, config, cancellationToken).ConfigureAwait(false);
                if (login.Status == HttpStatusCode.Forbidden)
                {
                    throw new QBittorrentException("qBittorrent временно заблокировал адрес сервера после неудачных попыток входа.");
                }

                if (!QBittorrentProtocol.IsLoginAccepted(login.Body) || login.Cookie is null)
                {
                    throw new QBittorrentException("qBittorrent отклонил логин или пароль из настроек плагина.");
                }

                state = new SessionState(key, login.Cookie);
            }

            _session = state;
            return state;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new QBittorrentException("qBittorrent не отвечает. Проверьте, что он запущен и доступен по сети.", ex);
        }
        finally
        {
            _loginGate.Release();
        }
    }

    private async Task<DiagnosticReport> DiagnoseCoreAsync(PluginConfiguration config, List<DiagnosticStep> steps, CancellationToken cancellationToken)
    {
        var baseUri = ResolveBase(config);
        if (baseUri is null)
        {
            steps.Add(new DiagnosticStep("Адрес", false, "Не задан корректный адрес Web UI qBittorrent, например http://192.168.1.20:8080."));
            return new DiagnosticReport(false, steps);
        }

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
            _session = new SessionState(SettingsKey(config), cookie);
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

    private sealed record SessionState(string SettingsKey, string? Cookie);
}
