using Jellyfin.Plugin.RuTracker.Configuration;
using Xunit;

namespace Jellyfin.Plugin.RuTracker.Tests;

public class PathRulesTests
{
    [Theory]
    [InlineData(@"C:\video", true)]
    [InlineData(@"C:\", true)]
    [InlineData("c:/video/Фильмы", true)]
    [InlineData(@"\\NAS\media\Movies", true)]
    [InlineData(@"\\NAS", false)]
    [InlineData("video", false)]
    [InlineData(@"C:video", false)]
    [InlineData(@"C:\video\..\Windows", false)]
    [InlineData(@"C:\vi:deo", false)]
    [InlineData(@"C:\video\name.", false)]
    [InlineData("", false)]
    public void IsAbsoluteWindowsPath(string path, bool expected)
        => Assert.Equal(expected, PathRules.IsAbsoluteWindowsPath(path));

    [Theory]
    [InlineData("/data/video", true)]
    [InlineData("/", true)]
    [InlineData("data/video", false)]
    [InlineData("/data/../etc", false)]
    [InlineData("/data/./video", false)]
    [InlineData("", false)]
    public void IsAbsoluteLinuxPath(string path, bool expected)
        => Assert.Equal(expected, PathRules.IsAbsoluteLinuxPath(path));
}
