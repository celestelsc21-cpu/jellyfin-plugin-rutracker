using Jellyfin.Plugin.RuTracker.Configuration;
using Xunit;

namespace Jellyfin.Plugin.RuTracker.Tests;

public class PathMapperTests
{
    private static PathMapper Create(params (string Local, string Remote)[] pairs)
        => new(System.Array.ConvertAll(pairs, p => new PathMapping { JellyfinPrefix = p.Local, QBittorrentPrefix = p.Remote }));

    [Theory]
    [InlineData("/data/video", @"C:\video")]
    [InlineData("/data/video/", @"C:\video")]
    [InlineData("/data/video/Фильмы", @"C:\video\Фильмы")]
    [InlineData("/data/video/Сериалы/Show (2020)/Season 01", @"C:\video\Сериалы\Show (2020)\Season 01")]
    public void ToQBittorrent_MapsUnderPrefix(string local, string expected)
    {
        var mapper = Create(("/data/video", @"C:\video"));

        Assert.True(mapper.TryToQBittorrent(local, out var remote));
        Assert.Equal(expected, remote);
    }

    [Theory]
    [InlineData("/data/videos")] // shares a string prefix but is a different folder
    [InlineData("/data")]
    [InlineData("/media/video")]
    [InlineData("relative/path")]
    [InlineData("/data/video/../etc")]
    public void ToQBittorrent_RejectsUnmappedOrUnsafe(string local)
    {
        var mapper = Create(("/data/video", @"C:\video"));

        Assert.False(mapper.TryToQBittorrent(local, out _));
    }

    [Theory]
    [InlineData(@"C:\video\Фильмы\a.mkv", "/data/video/Фильмы/a.mkv")]
    [InlineData(@"c:\VIDEO\x", "/data/video/x")] // Windows paths are case-insensitive
    [InlineData("C:/video/x", "/data/video/x")] // qBittorrent may report forward slashes
    [InlineData(@"C:\video", "/data/video")]
    public void ToJellyfin_MapsUnderPrefix(string remote, string expected)
    {
        var mapper = Create(("/data/video", @"C:\video"));

        Assert.True(mapper.TryToJellyfin(remote, out var local));
        Assert.Equal(expected, local);
    }

    [Fact]
    public void LongestPrefixWins()
    {
        var mapper = Create(("/data/video", @"C:\video"), ("/data/video/serials", @"D:\serials"));

        Assert.True(mapper.TryToQBittorrent("/data/video/serials/Show", out var remote));
        Assert.Equal(@"D:\serials\Show", remote);
        Assert.True(mapper.TryToJellyfin(@"D:\serials\Show", out var local));
        Assert.Equal("/data/video/serials/Show", local);
    }

    [Fact]
    public void UncShareAndDriveRoot_AreSupported()
    {
        var mapper = Create(("/data/nas", @"\\NAS\media"), ("/data/e", @"E:\"));

        Assert.True(mapper.TryToQBittorrent("/data/nas/Movies", out var unc));
        Assert.Equal(@"\\NAS\media\Movies", unc);
        Assert.True(mapper.TryToQBittorrent("/data/e/Movies", out var root));
        Assert.Equal(@"E:\Movies", root);
        Assert.True(mapper.TryToJellyfin(@"E:\Movies", out var back));
        Assert.Equal("/data/e/Movies", back);
    }

    [Fact]
    public void InvalidMappings_AreIgnored()
    {
        var mapper = Create(("data/video", @"C:\video"), ("/data/x", "video"));

        Assert.False(mapper.TryToQBittorrent("/data/x/a", out _));
    }
}
