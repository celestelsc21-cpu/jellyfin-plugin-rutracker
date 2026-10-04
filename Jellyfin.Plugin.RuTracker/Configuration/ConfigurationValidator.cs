using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.RuTracker.Configuration;

/// <summary>
/// Validates <see cref="PluginConfiguration"/>. Pure logic: file system access
/// is injected so the validator can be unit-tested.
/// </summary>
internal static partial class ConfigurationValidator
{
    private static readonly string[] AllowedRuTrackerHosts = ["rutracker.org", "rutracker.net", "rutracker.nl"];

    /// <summary>
    /// Validates the configuration.
    /// </summary>
    /// <param name="config">Configuration to check.</param>
    /// <param name="directoryExists">Checks a Jellyfin-side directory; pass <c>null</c> to skip.</param>
    /// <returns>Errors (blocking) and warnings (informational).</returns>
    public static ConfigurationValidationResult Validate(PluginConfiguration config, Func<string, bool>? directoryExists)
    {
        ArgumentNullException.ThrowIfNull(config);
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!IsAllowedRuTrackerUrl(config.RuTrackerBaseUrl))
        {
            errors.Add("Адрес RuTracker должен быть https-адресом rutracker.org / .net / .nl.");
        }

        if (string.IsNullOrWhiteSpace(config.RuTrackerSessionCookie)
            && (string.IsNullOrWhiteSpace(config.RuTrackerUsername) || string.IsNullOrEmpty(config.RuTrackerPassword)))
        {
            warnings.Add("Не заданы учётные данные RuTracker: поиск и скачивание .torrent работать не будут.");
        }

        if (string.IsNullOrWhiteSpace(config.QBittorrentUrl))
        {
            warnings.Add("Не задан адрес qBittorrent: загрузки недоступны.");
        }
        else if (!IsHttpUrl(config.QBittorrentUrl))
        {
            errors.Add("Адрес qBittorrent должен быть абсолютным http(s)-адресом без логина и параметров в URL.");
        }

        if (!CategoryPattern().IsMatch(config.QBittorrentCategory ?? string.Empty))
        {
            errors.Add("Категория qBittorrent: 1–64 символа, латиница, цифры, '-' и '_'.");
        }

        if (config.UpdateCheckIntervalHours is < 1 or > 168)
        {
            errors.Add("Интервал проверки обновлений: от 1 до 168 часов.");
        }

        ValidateMappings(config, errors, warnings);
        var mapper = new PathMapper(config.PathMappings ?? []);
        ValidateTargets(config, mapper, directoryExists, errors, warnings);

        if (config.Placement == PlacementMode.CopyAfterComplete)
        {
            ValidateMappedFolder(config.StagingJellyfinPath, "Промежуточная папка", mapper, directoryExists, errors);
        }

        return new ConfigurationValidationResult(errors, warnings);
    }

    /// <summary>
    /// Checks a RuTracker base URL against the host allow-list (SSRF protection).
    /// </summary>
    /// <param name="url">URL to check.</param>
    /// <returns><c>true</c> if allowed.</returns>
    public static bool IsAllowedRuTrackerUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort)
        {
            return false;
        }

        var host = uri.IdnHost;
        return AllowedRuTrackerHosts.Any(h =>
            host.Equals(h, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the Russian display name of a media kind.
    /// </summary>
    /// <param name="kind">Media kind.</param>
    /// <returns>Display name.</returns>
    internal static string KindName(MediaKind kind) => kind switch
    {
        MediaKind.Movie => "Фильм",
        MediaKind.Series => "Сериал",
        MediaKind.Show => "Передача",
        _ => kind.ToString()
    };

    private static bool IsHttpUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query);

    private static void ValidateMappings(PluginConfiguration config, List<string> errors, List<string> warnings)
    {
        var mappings = config.PathMappings ?? [];
        if (mappings.Length == 0)
        {
            warnings.Add("Не задано ни одного соответствия путей Jellyfin ↔ qBittorrent.");
        }

        for (var i = 0; i < mappings.Length; i++)
        {
            var n = (i + 1).ToString(CultureInfo.InvariantCulture);
            if (!PathRules.IsAbsoluteLinuxPath(mappings[i].JellyfinPrefix))
            {
                errors.Add($"Соответствие №{n}: путь Jellyfin должен быть абсолютным (/data/video).");
            }

            if (!PathRules.IsAbsoluteWindowsPath(mappings[i].QBittorrentPrefix))
            {
                errors.Add($"Соответствие №{n}: путь qBittorrent должен быть абсолютным путём Windows (C:\\video или \\\\сервер\\ресурс).");
            }
        }

        foreach (var dup in mappings
                     .Where(m => PathRules.IsAbsoluteLinuxPath(m.JellyfinPrefix))
                     .GroupBy(m => m.JellyfinPrefix.TrimEnd('/'), StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            errors.Add($"Путь Jellyfin {dup.Key} указан в нескольких соответствиях.");
        }
    }

    private static void ValidateTargets(
        PluginConfiguration config,
        PathMapper mapper,
        Func<string, bool>? directoryExists,
        List<string> errors,
        List<string> warnings)
    {
        var targets = config.DownloadTargets ?? [];

        foreach (var duplicate in targets.GroupBy(t => t.Id).Where(g => g.Count() > 1))
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"Повторяющийся идентификатор папки загрузки: {duplicate.Key}."));
        }

        foreach (var target in targets)
        {
            var label = string.IsNullOrWhiteSpace(target.Name) ? "(без имени)" : target.Name;
            if (string.IsNullOrWhiteSpace(target.Name) || target.Name.Length > 100)
            {
                errors.Add($"Папка {label}: название обязательно, до 100 символов.");
            }

            ValidateMappedFolder(target.JellyfinPath, $"Папка {label}", mapper, directoryExists, errors);
        }

        foreach (var kind in Enum.GetValues<MediaKind>())
        {
            var ofKind = targets.Where(t => t.Kind == kind).ToList();
            if (ofKind.Count == 0)
            {
                warnings.Add($"Нет папки загрузки для типа «{KindName(kind)}».");
            }
            else if (ofKind.Count(t => t.IsDefault) > 1)
            {
                errors.Add($"Для типа «{KindName(kind)}» отмечено несколько папок по умолчанию.");
            }
        }
    }

    private static void ValidateMappedFolder(
        string? path,
        string label,
        PathMapper mapper,
        Func<string, bool>? directoryExists,
        List<string> errors)
    {
        if (!PathRules.IsAbsoluteLinuxPath(path))
        {
            errors.Add($"{label}: нужен абсолютный путь внутри контейнера Jellyfin (/data/video/...).");
            return;
        }

        if (!mapper.TryToQBittorrent(path!, out _))
        {
            errors.Add($"{label}: путь {path} не покрыт ни одним соответствием путей — qBittorrent не узнает, куда сохранять.");
        }

        if (directoryExists is not null && !directoryExists(path!))
        {
            errors.Add($"{label}: каталог {path} не виден из контейнера Jellyfin.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex CategoryPattern();
}
