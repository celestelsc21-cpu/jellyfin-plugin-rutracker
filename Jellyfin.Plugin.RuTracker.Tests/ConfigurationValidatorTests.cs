using System;
using Jellyfin.Plugin.RuTracker.Configuration;
using Xunit;

namespace Jellyfin.Plugin.RuTracker.Tests;

public class ConfigurationValidatorTests
{
    private static PluginConfiguration ValidConfig() => new()
    {
        RuTrackerUsername = "user",
        RuTrackerPassword = "pass",
        QBittorrentUrl = "http://192.168.1.20:8080",
        PathMappings = [new PathMapping { JellyfinPrefix = "/data/video", QBittorrentPrefix = @"C:\video" }],
        DownloadTargets =
        [
            new DownloadTarget { Kind = MediaKind.Movie, Name = "Фильмы", JellyfinPath = "/data/video/Фильмы", IsDefault = true },
            new DownloadTarget { Kind = MediaKind.Series, Name = "Сериалы", JellyfinPath = "/data/video/Сериалы", IsDefault = true },
            new DownloadTarget { Kind = MediaKind.Show, Name = "Передачи", JellyfinPath = "/data/video/Передачи", IsDefault = true }
        ]
    };

    [Fact]
    public void ValidConfig_HasNoErrorsOrWarnings()
    {
        var result = ConfigurationValidator.Validate(ValidConfig(), _ => true);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void DefaultConfig_IsValidButWarns()
    {
        var result = ConfigurationValidator.Validate(new PluginConfiguration(), null);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void TargetOutsideMappings_IsError()
    {
        var config = ValidConfig();
        config.DownloadTargets[0].JellyfinPath = "/media/movies";

        var result = ConfigurationValidator.Validate(config, null);

        Assert.Contains(result.Errors, e => e.Contains("не покрыт", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingDirectory_IsError()
    {
        var result = ConfigurationValidator.Validate(ValidConfig(), p => p != "/data/video/Сериалы");

        Assert.Single(result.Errors);
    }

    [Fact]
    public void TwoDefaultsForOneKind_IsError()
    {
        var config = ValidConfig();
        config.DownloadTargets =
        [
            .. config.DownloadTargets,
            new DownloadTarget { Kind = MediaKind.Movie, Name = "Фильмы 2", JellyfinPath = "/data/video/Фильмы2", IsDefault = true }
        ];

        Assert.False(ConfigurationValidator.Validate(config, null).IsValid);
    }

    [Fact]
    public void CopyMode_RequiresMappedStaging()
    {
        var config = ValidConfig();
        config.Placement = PlacementMode.CopyAfterComplete;
        Assert.False(ConfigurationValidator.Validate(config, null).IsValid);

        config.StagingJellyfinPath = "/data/video/_incoming";
        Assert.True(ConfigurationValidator.Validate(config, null).IsValid);
    }

    [Theory]
    [InlineData("https://rutracker.org", true)]
    [InlineData("https://rutracker.net/", true)]
    [InlineData("https://www.rutracker.org", true)]
    [InlineData("http://rutracker.org", false)]
    [InlineData("https://rutracker.org.evil.com", false)]
    [InlineData("https://evilrutracker.org", false)]
    [InlineData("https://127.0.0.1", false)]
    [InlineData("https://rutracker.org:8443", false)]
    [InlineData("https://user:pw@rutracker.org", false)]
    public void RuTrackerUrl_AllowList(string url, bool expected)
        => Assert.Equal(expected, ConfigurationValidator.IsAllowedRuTrackerUrl(url));

    [Theory]
    [InlineData("ftp://192.168.1.20")]
    [InlineData("192.168.1.20:8080")]
    [InlineData("http://admin:pw@192.168.1.20:8080")]
    public void BadQBittorrentUrl_IsError(string url)
    {
        var config = ValidConfig();
        config.QBittorrentUrl = url;

        Assert.False(ConfigurationValidator.Validate(config, null).IsValid);
    }
}
