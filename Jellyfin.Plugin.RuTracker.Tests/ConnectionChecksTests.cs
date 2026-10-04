using System;
using System.Linq;
using Jellyfin.Plugin.RuTracker.Configuration;
using Jellyfin.Plugin.RuTracker.QBittorrent;
using Jellyfin.Plugin.RuTracker.Tracker;
using Xunit;

namespace Jellyfin.Plugin.RuTracker.Tests;

public class ConnectionChecksTests
{
    [Fact]
    public void BlockPage_IsNotRuTracker()
    {
        const string IspStub = "<html><head><title>Доступ ограничен</title></head><body>Ресурс заблокирован по решению ...</body></html>";

        Assert.False(TrackerHtmlParser.LooksLikeRuTracker(IspStub));
        Assert.True(TrackerHtmlParser.LooksLikeRuTracker("<a href=\"https://rutracker.org/forum/index.php\">"));
        Assert.Equal("Доступ ограничен", TrackerHtmlParser.ExtractTitle(IspStub));
    }

    [Fact]
    public void ExtractTitle_DecodesAndShortens()
    {
        Assert.Equal("Tom & Jerry", TrackerHtmlParser.ExtractTitle("<TITLE>\n Tom &amp; Jerry \n</TITLE>"));
        Assert.Equal(81, TrackerHtmlParser.ExtractTitle("<title>" + new string('x', 300) + "</title>").Length);
        Assert.Equal(string.Empty, TrackerHtmlParser.ExtractTitle("<html></html>"));
    }

    [Fact]
    public void ExtractLoginError_ReadsSiteMessage()
    {
        const string Page = "<html><body>rutracker<h4 class=\"warnColor1 tCenter mrg_16\">Неверное имя пользователя или пароль</h4></body></html>";

        Assert.Equal("Неверное имя пользователя или пароль", TrackerHtmlParser.ExtractLoginError(Page));
        Assert.Null(TrackerHtmlParser.ExtractLoginError("<html><body>rutracker</body></html>"));
    }

    [Fact]
    public void BaseUris_MainThenMirror_SkippingInvalidAndDuplicates()
    {
        var config = new PluginConfiguration { RuTrackerBaseUrl = "https://rutracker.org/", RuTrackerMirrorUrl = "https://rutracker.net" };
        Assert.Equal(new[] { "rutracker.org", "rutracker.net" }, RuTrackerUrls.GetBaseUris(config).Select(u => u.Host));

        config.RuTrackerMirrorUrl = "https://evil.example";
        Assert.Equal(new[] { "rutracker.org" }, RuTrackerUrls.GetBaseUris(config).Select(u => u.Host));

        config.RuTrackerMirrorUrl = "https://RUTRACKER.org";
        Assert.Single(RuTrackerUrls.GetBaseUris(config));

        config.RuTrackerBaseUrl = string.Empty;
        config.RuTrackerMirrorUrl = string.Empty;
        Assert.Empty(RuTrackerUrls.GetBaseUris(config));
    }

    [Fact]
    public void PreferHost_PutsLastWorkingFirst()
    {
        var bases = new[] { new Uri("https://rutracker.org/"), new Uri("https://rutracker.net/") };

        Assert.Equal("rutracker.net", RuTrackerUrls.PreferHost(bases, "rutracker.net")[0].Host);
        Assert.Equal("rutracker.org", RuTrackerUrls.PreferHost(bases, null)[0].Host);
        Assert.Equal(2, RuTrackerUrls.PreferHost(bases, "unknown.host").Count);
    }

    [Theory]
    [InlineData("https://rutracker.org/forum/index.php", true)]
    [InlineData("https://rutracker.net/forum/login.php", true)]
    [InlineData("http://rutracker.org/forum/index.php", false)] // downgrade to http
    [InlineData("https://blocked.provider.example/stub", false)]
    [InlineData("https://rutracker.org.evil.example/", false)]
    public void Redirects_StayOnRuTracker(string target, bool allowed)
        => Assert.Equal(allowed, RuTrackerUrls.IsAllowedRedirect(new Uri(target)));

    [Fact]
    public void Validator_ChecksMirror()
    {
        var config = new PluginConfiguration { RuTrackerMirrorUrl = "https://evil.example" };
        Assert.Contains(ConfigurationValidator.Validate(config, null).Errors, e => e.Contains("Альтернативный", StringComparison.Ordinal));

        config.RuTrackerMirrorUrl = string.Empty;
        Assert.True(ConfigurationValidator.Validate(config, null).IsValid);

        config.RuTrackerMirrorUrl = "https://rutracker.org/";
        Assert.Contains(ConfigurationValidator.Validate(config, null).Warnings, w => w.Contains("совпадает", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Ok.", true)]
    [InlineData("Ok.\n", true)]
    [InlineData("Fails.", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void QBittorrent_LoginBody(string? body, bool accepted)
        => Assert.Equal(accepted, QBittorrentProtocol.IsLoginAccepted(body));

    [Fact]
    public void QBittorrent_SessionCookie()
    {
        Assert.Equal(
            "SID=AbCdEf0123456789",
            QBittorrentProtocol.ExtractSessionCookie(["SID=AbCdEf0123456789; HttpOnly; SameSite=Strict; path=/"]));
        Assert.Equal(
            "QBT_SID_8080=AbCdEf0123456789",
            QBittorrentProtocol.ExtractSessionCookie(["theme=dark1234567", "QBT_SID_8080=AbCdEf0123456789; path=/"]));
        Assert.Null(QBittorrentProtocol.ExtractSessionCookie(["SID=bad value\r\n; path=/"]));
        Assert.Null(QBittorrentProtocol.ExtractSessionCookie([]));
    }

    [Fact]
    public void QBittorrent_SavePath()
    {
        Assert.Equal(@"C:\video\Downloads", QBittorrentProtocol.ReadSavePath("{\"save_path\":\"C:\\\\video\\\\Downloads\",\"locale\":\"ru\"}"));
        Assert.Null(QBittorrentProtocol.ReadSavePath("{}"));
        Assert.Null(QBittorrentProtocol.ReadSavePath("not json"));
    }
}
