using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Jellyfin.Plugin.RuTracker.Configuration;
using Jellyfin.Plugin.RuTracker.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RuTracker.Tracker;

/// <summary>
/// RuTracker client. All site requests are serialized and spaced out to respect
/// the site. Sessions are kept in memory per address and renewed on expiry.
/// When an address is unreachable (network error, 5xx, ISP block page) the
/// alternative address is tried.
/// </summary>
internal sealed class RuTrackerClient : IRuTrackerClient, IDisposable
{
    /// <summary>
    /// Named <see cref="HttpClient"/> registered for RuTracker.
    /// </summary>
    public const string HttpClientName = "RuTracker";

    private const int MaxRedirects = 3;
    private const string IndexPage = "forum/index.php";

    private static readonly Encoding Cp1251 = CreateCp1251();
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SearchCacheTtl = TimeSpan.FromMinutes(10);

    // After RuTracker refuses a password login (captcha, 403) further attempts only
    // prolong the block, so they are paused for a while.
    private static readonly TimeSpan LoginBackoff = TimeSpan.FromMinutes(15);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPluginConfigurationAccessor _config;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RuTrackerClient> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Guarded by _gate.
    private readonly Dictionary<string, string> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTimeOffset Until, string Reason)> _loginBlocked = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private string? _settingsKey;
    private string? _preferredHost;

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

        var html = await GetAuthenticatedPageAsync(RuTrackerUrls.SearchPath(normalized), cancellationToken).ConfigureAwait(false);
        var results = TrackerHtmlParser.ParseSearchResults(html);

        _logger.LogDebug("RuTracker search returned {Count} rows", results.Count);
        _cache.Set(cacheKey, results, SearchCacheTtl);
        return results;
    }

    /// <inheritdoc />
    public async Task<DiagnosticReport> DiagnoseAsync(CancellationToken cancellationToken)
    {
        var config = _config.Current;
        var steps = new List<DiagnosticStep>();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ResetIfSettingsChanged(config);
            var bases = RuTrackerUrls.GetBaseUris(config);
            if (bases.Count == 0)
            {
                steps.Add(new DiagnosticStep("Адрес", false, "Не задан корректный адрес RuTracker."));
                return new DiagnosticReport(false, steps);
            }

            var anyOk = false;
            foreach (var baseUri in bases)
            {
                try
                {
                    if (await DiagnoseHostAsync(config, baseUri, steps, cancellationToken).ConfigureAwait(false))
                    {
                        anyOk = true;
                        _preferredHost ??= baseUri.Host;
                    }
                }
                catch (RuTrackerException ex)
                {
                    steps.Add(new DiagnosticStep(baseUri.Host + ": проверка", false, ex.Message));
                }
            }

            return new DiagnosticReport(anyOk, steps);
        }
        finally
        {
            _gate.Release();
        }
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
        var raw = string.Join(
            '\n',
            config.RuTrackerBaseUrl,
            config.RuTrackerMirrorUrl,
            config.RuTrackerUsername,
            config.RuTrackerPassword,
            config.RuTrackerSessionCookie);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static bool HasCredentials(PluginConfiguration config)
        => !string.IsNullOrWhiteSpace(config.RuTrackerUsername) && !string.IsNullOrEmpty(config.RuTrackerPassword);

    private void ResetIfSettingsChanged(PluginConfiguration config)
    {
        var key = SettingsKey(config);
        if (!string.Equals(key, _settingsKey, StringComparison.Ordinal))
        {
            _sessions.Clear();
            _loginBlocked.Clear();
            _preferredHost = null;
            _settingsKey = key;
        }
    }

    private async Task<string> GetAuthenticatedPageAsync(string relative, CancellationToken cancellationToken)
    {
        var config = _config.Current;
        var cloudflareCookie = CloudflareCookie.Normalize(config.RuTrackerCloudflareCookie);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ResetIfSettingsChanged(config);
            var bases = RuTrackerUrls.PreferHost(RuTrackerUrls.GetBaseUris(config), _preferredHost);
            if (bases.Count == 0)
            {
                throw new RuTrackerException("Адрес RuTracker в настройках плагина некорректен.");
            }

            RuTrackerUnavailableException? lastUnavailable = null;
            foreach (var baseUri in bases)
            {
                try
                {
                    var html = await GetAuthenticatedOnHostAsync(config, baseUri, relative, cancellationToken).ConfigureAwait(false);
                    _preferredHost = baseUri.Host;
                    return html;
                }
                catch (RuTrackerUnavailableException ex)
                {
                    lastUnavailable = ex;
                    _logger.LogWarning("RuTracker address {Host} is unavailable: {Reason}", baseUri.Host, ex.Message);
                }
            }

            throw lastUnavailable!;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> GetAuthenticatedOnHostAsync(PluginConfiguration config, Uri baseUri, string relative, CancellationToken cancellationToken)
    {
        var host = baseUri.Host;
        if (!_sessions.TryGetValue(host, out var session))
        {
            session = SessionCookie.Normalize(config.RuTrackerSessionCookie)
                ?? await LoginAsync(config, baseUri, cloudflareCookie, cancellationToken).ConfigureAwait(false);
            _sessions[host] = session;
        }

        var html = await GetPageAsync(baseUri, relative, session, cloudflareCookie, cancellationToken).ConfigureAwait(false);
        if (TrackerHtmlParser.IsLoggedIn(html))
        {
            return html;
        }

        // The page did not show a logged-in user. If the session still works on the
        // index page, the problem is this particular request, not the login: report
        // what RuTracker answered instead of logging in again.
        var index = await GetPageAsync(baseUri, IndexPage, session, cloudflareCookie, cancellationToken).ConfigureAwait(false);
        if (TrackerHtmlParser.IsLoggedIn(index))
        {
            var title = TrackerHtmlParser.ExtractTitle(html);
            var text = TrackerHtmlParser.ExtractVisibleText(html, 160);
            _logger.LogWarning(
                "RuTracker page {Page} on {Host} opened as guest while the session is valid (title {Title})",
                relative.Split('?', 2)[0],
                host,
                title);
            throw new RuTrackerException(
                "RuTracker не выдал результаты"
                + (title.Length > 0 ? ": «" + title + "»" : string.Empty)
                + (text.Length > 0 ? " — " + text : string.Empty));
        }

        // Session expired or the pasted cookie is stale: log in once more.
        _logger.LogInformation(
            "RuTracker page {Page} on {Host} opened as guest (title {Title}), logging in again",
            relative.Split('?', 2)[0],
            host,
            TrackerHtmlParser.ExtractTitle(html));
        _sessions.Remove(host);
        try
        {
            session = await LoginAsync(config, baseUri, cloudflareCookie, cancellationToken).ConfigureAwait(false);
        }
        catch (RuTrackerException ex) when (ex is not RuTrackerUnavailableException && SessionCookie.Normalize(config.RuTrackerSessionCookie) is not null)
        {
            throw new RuTrackerException(
                "RuTracker не узнал сессию из cookie bb_session, а вход по паролю не удался. "
                + "Обновите cookie в настройках плагина (войдите на сайт в браузере и скопируйте bb_session заново). "
                + "Подробности: " + ex.Message,
                ex);
        }

        _sessions[host] = session;
        html = await GetPageAsync(baseUri, relative, session, cloudflareCookie, cancellationToken).ConfigureAwait(false);
        if (TrackerHtmlParser.IsLoggedIn(html))
        {
            return html;
        }

        _sessions.Remove(host);
        throw new RuTrackerException("RuTracker выдал сессию, но страницы открываются как для гостя. Нажмите «Проверить подключение» в настройках плагина.");
    }

    private async Task<bool> DiagnoseHostAsync(PluginConfiguration config, Uri baseUri, List<DiagnosticStep> steps, CancellationToken cancellationToken)
    {
        var host = baseUri.Host;

        // 1. Reachability: the site answers and it is really RuTracker.
        try
        {
            var cloudflareCookie = CloudflareCookie.Normalize(config.RuTrackerCloudflareCookie);
            var html = await GetPageAsync(baseUri, IndexPage, null, cloudflareCookie, cancellationToken).ConfigureAwait(false);
            var title = TrackerHtmlParser.ExtractTitle(html);
            steps.Add(new DiagnosticStep($"{host}: соединение", true, title.Length > 0 ? $"Сайт отвечает: «{title}»." : "Сайт отвечает."));
        }
        catch (RuTrackerUnavailableException ex)
        {
            steps.Add(new DiagnosticStep($"{host}: соединение", false, ex.Message));
            return false;
        }

        // 2. Session from the pasted cookie, if any.
        string? session = null;
        var cookie = SessionCookie.Normalize(config.RuTrackerSessionCookie);
        var cloudflareCookie = CloudflareCookie.Normalize(config.RuTrackerCloudflareCookie);
        if (cookie is not null)
        {
            var html = await GetPageAsync(baseUri, IndexPage, cookie, cloudflareCookie, cancellationToken).ConfigureAwait(false);
            if (TrackerHtmlParser.IsLoggedIn(html))
            {
                session = cookie;
                steps.Add(new DiagnosticStep($"{host}: вход по cookie", true, "Сессия из cookie bb_session действительна."));
            }
            else
            {
                steps.Add(new DiagnosticStep($"{host}: вход по cookie", false, "Cookie bb_session недействительна или устарела."));
            }
        }
        else if (!string.IsNullOrWhiteSpace(config.RuTrackerSessionCookie))
        {
            steps.Add(new DiagnosticStep($"{host}: вход по cookie", false, "Значение cookie bb_session имеет неверный формат."));
        }

        // 3. Login with username and password.
        if (session is null)
        {
            if (!HasCredentials(config))
            {
                steps.Add(new DiagnosticStep($"{host}: вход", false, "Логин и пароль RuTracker не заданы."));
                return false;
            }

            try
            {
                session = await LoginAsync(config, baseUri, cloudflareCookie, cancellationToken).ConfigureAwait(false);
                steps.Add(new DiagnosticStep($"{host}: вход по логину и паролю", true, "RuTracker принял логин и пароль."));
            }
            catch (RuTrackerException ex)
            {
                steps.Add(new DiagnosticStep($"{host}: вход по логину и паролю", false, ex.Message));
                return false;
            }

            // 4. The session really works.
            var html = await GetPageAsync(baseUri, IndexPage, session, cloudflareCookie, cancellationToken).ConfigureAwait(false);
            if (!TrackerHtmlParser.IsLoggedIn(html))
            {
                steps.Add(new DiagnosticStep($"{host}: проверка сессии", false, "После входа страницы открываются как для гостя."));
                return false;
            }

            steps.Add(new DiagnosticStep($"{host}: проверка сессии", true, "Сессия действует."));
        }

        // 5. The search page itself (it may behave differently from the index page).
        // A Cyrillic query exercises the query-string encoding as well.
        var search = await GetPageAsync(baseUri, RuTrackerUrls.SearchPath("Матрица"), session, cloudflareCookie, cancellationToken)
            .ConfigureAwait(false);
        if (!TrackerHtmlParser.IsLoggedIn(search))
        {
            var seen = TrackerHtmlParser.ExtractTitle(search);
            steps.Add(new DiagnosticStep(
                $"{host}: страница поиска",
                false,
                "Страница поиска открылась как для гостя" + (seen.Length > 0 ? " («" + seen + "»)" : string.Empty) + "."));
            return false;
        }

        var found = TrackerHtmlParser.ParseSearchResults(search).Count;
        steps.Add(new DiagnosticStep(
            $"{host}: страница поиска",
            true,
            "Поиск работает: по тестовому запросу «Матрица» найдено раздач — " + found.ToString(CultureInfo.InvariantCulture) + "."));

        _sessions[host] = session;
        return true;
    }

    private async Task<string> LoginAsync(PluginConfiguration config, Uri baseUri, string? cloudflareCookie, CancellationToken cancellationToken)
    {
        if (!HasCredentials(config))
        {
            throw new RuTrackerException(string.IsNullOrWhiteSpace(config.RuTrackerSessionCookie)
                ? "В настройках плагина не задан логин и пароль RuTracker."
                : "Cookie bb_session устарела, а логин и пароль RuTracker не заданы. Обновите cookie в настройках плагина.");
        }

        if (_loginBlocked.TryGetValue(baseUri.Host, out var blocked) && blocked.Until > DateTimeOffset.UtcNow)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((blocked.Until - DateTimeOffset.UtcNow).TotalMinutes));
            throw new RuTrackerException(
                blocked.Reason + " Плагин не повторяет вход по паролю ещё "
                + minutes.ToString(CultureInfo.InvariantCulture) + " мин., чтобы не продлевать блокировку.");
        }

        var loginUri = new Uri(baseUri, "forum/login.php");

        // 1. Open the login page first, like a browser: it sets service cookies
        //    and may carry hidden form fields or a captcha.
        Dictionary<string, string> cookies;
        IReadOnlyList<KeyValuePair<string, string>> hiddenFields;
        using (var page = await SendAsync(() => CreateRequest(HttpMethod.Get, loginUri, cloudflareCookie), cancellationToken).ConfigureAwait(false))
        {
            cookies = page.Headers.TryGetValues("Set-Cookie", out var pageCookies)
                ? CookieJar.Collect(pageCookies)
                : new Dictionary<string, string>(StringComparer.Ordinal);
            var html = Cp1251.GetString(await page.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
            if (TrackerHtmlParser.IsCloudflareChallenge(html))
            {
                throw new RuTrackerUnavailableException(
                    $"{baseUri.Host} требует проверку Cloudflare. Введите cookie cf_clearance из браузера в настройках плагина.");
            }

            if (!TrackerHtmlParser.LooksLikeRuTracker(html))
            {
                throw BlockPage(baseUri, html);
            }

            if (TrackerHtmlParser.HasCaptcha(html))
            {
                throw BlockLogin(baseUri, CaptchaRequired(baseUri));
            }

            hiddenFields = TrackerHtmlParser.ExtractLoginHiddenFields(html);
        }

        // 2. Submit the form with the same cookies, Referer and Origin a browser would send.
        var body = new StringBuilder();
        foreach (var field in hiddenFields)
        {
            body.Append(HttpUtility.UrlEncode(field.Key, Cp1251)).Append('=').Append(HttpUtility.UrlEncode(field.Value, Cp1251)).Append('&');
        }

        body.Append("login_username=").Append(HttpUtility.UrlEncode(config.RuTrackerUsername, Cp1251))
            .Append("&login_password=").Append(HttpUtility.UrlEncode(config.RuTrackerPassword, Cp1251))
            .Append("&login=").Append(HttpUtility.UrlEncode("вход", Cp1251));
        var bodyBytes = Encoding.ASCII.GetBytes(body.ToString());
        var cookieHeader = CookieJar.ToHeader(cookies);

        using var response = await SendAsync(
            () =>
            {
                var request = CreateRequest(HttpMethod.Post, loginUri, cloudflareCookie);
                request.Content = new ByteArrayContent(bodyBytes);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
                request.Headers.Referrer = loginUri;
                request.Headers.Add("Origin", baseUri.GetLeftPart(UriPartial.Authority));
                if (cookieHeader is not null)
                {
                    request.Headers.Add("Cookie", cookieHeader);
                }

                return request;
            },
            cancellationToken).ConfigureAwait(false);

        var session = response.Headers.TryGetValues("Set-Cookie", out var responseCookies)
            ? SessionCookie.FromSetCookie(responseCookies)
            : null;
        if (session is not null)
        {
            _logger.LogInformation("Logged in to RuTracker on {Host}", baseUri.Host);
            return session;
        }

        // 3. No session: explain why as precisely as possible.
        var status = (int)response.StatusCode;
        var responseHtml = Cp1251.GetString(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
        if (TrackerHtmlParser.IsCloudflareChallenge(responseHtml))
        {
            throw new RuTrackerUnavailableException(
                $"{baseUri.Host} требует проверку Cloudflare. Введите cookie cf_clearance из браузера в настройках плагина.");
        }

        if (responseHtml.Length > 0 && !TrackerHtmlParser.LooksLikeRuTracker(responseHtml))
        {
            throw BlockPage(baseUri, responseHtml);
        }

        if (TrackerHtmlParser.HasCaptcha(responseHtml))
        {
            throw BlockLogin(baseUri, CaptchaRequired(baseUri));
        }

        var siteError = TrackerHtmlParser.ExtractLoginError(responseHtml);
        var visible = siteError ?? TrackerHtmlParser.ExtractVisibleText(responseHtml, 160);
        var statusText = status.ToString(CultureInfo.InvariantCulture);
        _logger.LogWarning(
            "RuTracker login on {Host} rejected: status {Status}, cookies sent {CookieCount}, hidden fields {FieldCount}",
            baseUri.Host,
            status,
            cookies.Count,
            hiddenFields.Count);

        if (siteError is not null)
        {
            throw new RuTrackerException("RuTracker отклонил вход: " + siteError);
        }

        if (status == 403)
        {
            throw BlockLogin(baseUri, new RuTrackerException(
                "RuTracker запретил вход с адреса сервера (код 403)"
                + (visible.Length > 0 ? ": «" + visible + "»" : string.Empty)
                + ". Обычно так бывает после нескольких неудачных попыток: RuTracker временно требует капчу. "
                + "Войдите на rutracker.org в браузере и вставьте значение cookie bb_session в настройках плагина."));
        }

        throw new RuTrackerException(
            "RuTracker не выдал сессию (код " + statusText + ")"
            + (visible.Length > 0 ? ": «" + visible + "»" : string.Empty)
            + ". Проверьте логин и пароль.");
    }

    private RuTrackerException BlockLogin(Uri baseUri, RuTrackerException reason)
    {
        _loginBlocked[baseUri.Host] = (DateTimeOffset.UtcNow + LoginBackoff, reason.Message);
        return reason;
    }

    private RuTrackerException CaptchaRequired(Uri baseUri)
    {
        _logger.LogWarning("RuTracker login on {Host} requires a captcha", baseUri.Host);
        return new RuTrackerException(
            "RuTracker требует ввести капчу для входа с адреса сервера. Войдите на " + baseUri.Host
            + " в браузере и вставьте значение cookie bb_session в настройках плагина.");
    }

    private async Task<string> GetPageAsync(Uri baseUri, string relative, string? session, string? cloudflareCookie, CancellationToken cancellationToken)
    {
        var uri = new Uri(baseUri, relative);
        var redirects = 0;
        while (true)
        {
            var target = uri;
            using var response = await SendAsync(
                () =>
                {
                    var request = CreateRequest(HttpMethod.Get, target, cloudflareCookie);
                    if (session is not null)
                    {
                        request.Headers.Add("Cookie", SessionCookie.Name + "=" + session);
                    }

                    return request;
                },
                cancellationToken).ConfigureAwait(false);

            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400 && response.Headers.Location is not null)
            {
                var next = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(target, response.Headers.Location);
                if (!RuTrackerUrls.IsAllowedRedirect(next))
                {
                    throw new RuTrackerUnavailableException(
                        $"{baseUri.Host} перенаправляет на посторонний адрес {next.Host}. Похоже на блокировку провайдера.");
                }

                if (redirects >= MaxRedirects)
                {
                    throw new RuTrackerUnavailableException($"{baseUri.Host}: слишком много перенаправлений.");
                }

                uri = next;
                redirects++;
                continue;
            }

            var html = Cp1251.GetString(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
            if (!TrackerHtmlParser.LooksLikeRuTracker(html))
            {
                throw BlockPage(baseUri, html);
            }

            return html;
        }
    }

    private RuTrackerUnavailableException BlockPage(Uri baseUri, string html)
    {
        var title = TrackerHtmlParser.ExtractTitle(html);
        _logger.LogWarning("RuTracker address {Host} returned a foreign page titled {Title}", baseUri.Host, title);
        return new RuTrackerUnavailableException(title.Length > 0
            ? $"Вместо RuTracker пришла другая страница («{title}»). Вероятно, провайдер блокирует {baseUri.Host} для сервера."
            : $"Вместо RuTracker пришла другая страница. Вероятно, провайдер блокирует {baseUri.Host} для сервера.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string? cloudflareCookie)
    {
        var request = new HttpRequestMessage(method, uri);
        var userAgent = _config.Current.RuTrackerUserAgent;
        request.Headers.UserAgent.ParseAdd(
            string.IsNullOrWhiteSpace(userAgent)
                ? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36"
                : userAgent);
        AddCloudflareCookie(request, cloudflareCookie);
        return request;
    }

    private static void AddCloudflareCookie(HttpRequestMessage request, string? cloudflareCookie)
    {
        if (cloudflareCookie is not null)
        {
            request.Headers.Add("Cookie", "cf_clearance=" + cloudflareCookie);
        }
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
            var host = request.RequestUri?.Host ?? "RuTracker";
            try
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode < 500)
                {
                    return response;
                }

                var status = (int)response.StatusCode;
                response.Dispose();
                if (attempt >= Attempts)
                {
                    throw new RuTrackerUnavailableException(
                        host + " временно недоступен (ответ " + status.ToString(CultureInfo.InvariantCulture) + ").");
                }

                _logger.LogWarning("RuTracker {Host} answered {Status}, retrying", host, status);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                if (attempt >= Attempts)
                {
                    _logger.LogWarning(ex, "RuTracker {Host} is unreachable", host);
                    throw new RuTrackerUnavailableException(
                        ex is TaskCanceledException
                            ? $"{host} не ответил вовремя. Сервер, вероятно, не может достучаться до сайта."
                            : $"Не удаётся соединиться с {host}: {ex.Message}",
                        ex);
                }

                _logger.LogWarning("RuTracker {Host} request failed ({Error}), retrying", host, ex.GetType().Name);
            }

            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
        }
    }
}
