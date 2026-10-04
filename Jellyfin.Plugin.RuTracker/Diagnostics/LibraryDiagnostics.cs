using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.RuTracker.Access;
using Jellyfin.Plugin.RuTracker.Configuration;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Channels;

namespace Jellyfin.Plugin.RuTracker.Diagnostics;

/// <summary>
/// Checks what "watch while downloading" needs: the channel is registered and visible to
/// users, and Jellyfin can write into the download folders.
/// </summary>
internal static class LibraryDiagnostics
{
    private const string ChannelName = "RuTracker";
    private const string ProbeFileName = ".rutracker-write-test";

    /// <summary>
    /// Runs the checks.
    /// </summary>
    /// <param name="config">Plugin configuration.</param>
    /// <param name="channels">Channel manager.</param>
    /// <param name="users">User manager.</param>
    /// <param name="access">Plugin access service.</param>
    /// <returns>The report.</returns>
    public static async Task<DiagnosticReport> RunAsync(
        PluginConfiguration config,
        IChannelManager channels,
        IUserManager users,
        IAccessService access)
    {
        var steps = new List<DiagnosticStep>();

        var all = await channels.GetChannelsInternalAsync(new ChannelQuery()).ConfigureAwait(false);
        var registered = all.Items.Any(c => string.Equals(c.Name, ChannelName, StringComparison.OrdinalIgnoreCase));
        steps.Add(new DiagnosticStep(
            "Канал RuTracker зарегистрирован",
            registered,
            registered ? "Jellyfin видит канал плагина." : "Jellyfin не видит канал. Перезапустите сервер после обновления плагина."));

        foreach (var user in users.GetUsers().OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase))
        {
            var visible = false;
            try
            {
                var own = await channels.GetChannelsInternalAsync(new ChannelQuery { UserId = user.Id }).ConfigureAwait(false);
                visible = own.Items.Any(c => string.Equals(c.Name, ChannelName, StringComparison.OrdinalIgnoreCase));
            }
            catch (InvalidOperationException)
            {
                visible = false;
            }

            string detail;
            if (visible)
            {
                detail = "Плитка RuTracker есть в «Мои медиатеки».";
            }
            else if (!access.GetAccess(user.Id).CanSearch)
            {
                detail = "Нет роли «Поиск» в настройках плагина (раздел «Доступ»).";
            }
            else if (!user.HasPermission(PermissionKind.EnableAllChannels))
            {
                detail = "В профиле пользователя (вкладка «Доступ») не разрешены каналы: включите доступ ко всем каналам или отметьте RuTracker.";
            }
            else
            {
                detail = "Канал скрыт настройками пользователя или ограничением по возрасту.";
            }

            steps.Add(new DiagnosticStep("Пользователь «" + user.Username + "» видит канал", visible, detail));
        }

        var folders = (config.DownloadTargets ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t.JellyfinPath))
            .Select(t => (t.Name, Path: t.JellyfinPath))
            .ToList();
        if (config.Placement == PlacementMode.CopyAfterComplete && !string.IsNullOrWhiteSpace(config.StagingJellyfinPath))
        {
            folders.Add(("Промежуточная папка", config.StagingJellyfinPath));
        }

        foreach (var (name, path) in folders)
        {
            var (ok, detail) = CheckWritable(path);
            steps.Add(new DiagnosticStep("Запись в папку «" + name + "»", ok, detail));
        }

        return new DiagnosticReport(steps.All(s => s.Ok), steps);
    }

    /// <summary>
    /// Tries to create and delete a small file in a folder.
    /// </summary>
    /// <param name="folder">Folder as Jellyfin sees it.</param>
    /// <returns>Result and a user-facing explanation.</returns>
    public static (bool Ok, string Detail) CheckWritable(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return (false, "Папка " + folder + " не найдена в контейнере Jellyfin.");
        }

        var probe = Path.Combine(folder, ProbeFileName);
        try
        {
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return (true, "Jellyfin может записывать в " + folder + ".");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, "Jellyfin не может записывать в " + folder + " (" + ex.Message + "). "
                + "Без записи недокачанные серии не скрываются из медиатеки, а режим копирования не работает. "
                + "Подключите эту папку в контейнер Jellyfin с правом записи (без «:ro»).");
        }
    }
}
