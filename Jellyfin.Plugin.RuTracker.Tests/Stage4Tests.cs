using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web;
using Jellyfin.Plugin.RuTracker.Configuration;
using Jellyfin.Plugin.RuTracker.Library;
using Jellyfin.Plugin.RuTracker.Streaming;
using Jellyfin.Plugin.RuTracker.Torrents;
using Jellyfin.Plugin.RuTracker.Tracker;
using Xunit;

namespace Jellyfin.Plugin.RuTracker.Tests;

public class Stage4Tests
{
    [Theory]
    [InlineData("Во все тяжкие / Breaking Bad / Сезон: 1 / Серии: 1-7 из 7 (Винс Гиллиган) [2008, США, драма, WEB-DL 1080p]", "Breaking Bad (2008)")]
    [InlineData("Брат (Алексей Балабанов) [1997, Россия, драма, DVDRip]", "Брат (1997)")]
    [InlineData("", "RuTracker 5")]
    public void ReleaseTitle_FolderName(string title, string expected)
    {
        Assert.Equal(expected, ReleaseTitle.FolderName(title, "RuTracker 5"));
    }

    [Fact]
    public void ReleaseTitle_Sanitize_RemovesInvalidCharacters()
    {
        Assert.Equal("a b c", ReleaseTitle.Sanitize("a:b?c."));
        Assert.Equal(string.Empty, ReleaseTitle.Sanitize(".."));
    }

    [Fact]
    public void IgnoreFile_ListsBaseNamesEscaped()
    {
        var content = IgnoreFile.Build(["Show/e1.mkv", "Show\\e2 [1].mkv", "e1.mkv"]);
        Assert.Equal(IgnoreFile.Header + "\ne1.mkv\ne2 \\[1\\].mkv\n", content);
        Assert.True(IgnoreFile.IsManaged(content!));
        Assert.False(IgnoreFile.IsManaged("*.tmp\n"));
    }

    [Fact]
    public void IgnoreFile_NothingToHide_ReturnsNull()
    {
        Assert.Null(IgnoreFile.Build([]));
    }

    [Theory]
    [InlineData("S1/e1.mkv", "/data/video/A/S1/e1.mkv")]
    [InlineData("S1\\e1.mkv", "/data/video/A/S1/e1.mkv")]
    [InlineData("../e1.mkv", null)]
    [InlineData("S1/../../e1.mkv", null)]
    [InlineData("", null)]
    public void SafePath_Combine(string relative, string? expected)
    {
        Assert.Equal(expected, SafePath.Combine("/data/video/A", relative));
    }

    [Fact]
    public void PieceMath_ReadableFrom_StopsAtMissingPiece()
    {
        int[] partial = [2, 2, 2, 0, 2];
        Assert.Equal(20, PieceMath.ReadableFrom(partial, 0, 4, 10, 0, 50));
        Assert.Equal(0, PieceMath.ReadableFrom(partial, 0, 4, 10, 30, 50));

        int[] complete = [2, 2, 2, 2, 2];
        Assert.Equal(50, PieceMath.ReadableFrom(complete, 0, 4, 10, 0, 50));
        Assert.Equal(5, PieceMath.ReadableFrom(complete, 0, 4, 10, 45, 50));
    }

    [Theory]
    [InlineData(null, true, 0, 99)]
    [InlineData("bytes=10-19", true, 10, 19)]
    [InlineData("bytes=50-", true, 50, 99)]
    [InlineData("bytes=90-500", true, 90, 99)]
    [InlineData("bytes=-10", true, 90, 99)]
    [InlineData("bytes=200-", false, 0, 0)]
    [InlineData("bytes=0-1,5-6", true, 0, 99)]
    public void ByteRange_TryParse(string? header, bool ok, long start, long end)
    {
        var result = ByteRange.TryParse(header, 100, out var s, out var e);
        Assert.Equal(ok, result);
        if (ok)
        {
            Assert.Equal(start, s);
            Assert.Equal(end, e);
        }
    }

    [Fact]
    public void StreamTokens_ValidatesSignatureAndExpiry()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
        var tokens = new StreamTokens(new byte[32], clock);
        var id = Guid.NewGuid();
        var query = HttpUtility.ParseQueryString(tokens.Query(id, 3));
        var exp = long.Parse(query["exp"]!, CultureInfo.InvariantCulture);
        var sig = query["sig"];

        Assert.True(tokens.IsValid(id, 3, exp, sig));
        Assert.False(tokens.IsValid(id, 4, exp, sig));
        Assert.False(tokens.IsValid(Guid.NewGuid(), 3, exp, sig));
        Assert.False(tokens.IsValid(id, 3, exp + 1, sig));
        Assert.False(tokens.IsValid(id, 3, exp, null));

        clock.Now += StreamTokens.Lifetime + TimeSpan.FromMinutes(1);
        Assert.False(tokens.IsValid(id, 3, exp, sig));
    }

    [Fact]
    public void EpisodePlanner_FileIndexOfEpisode_UsesNaturalOrder()
    {
        var files = new List<TorrentFileEntry>
        {
            new(0, "Show/e10.mkv", 100),
            new(1, "Show/e2.mkv", 200),
            new(2, "Show/e2.srt", 5)
        };

        Assert.Equal(1, EpisodePlanner.FileIndexOfEpisode(files, 1));
        Assert.Equal(0, EpisodePlanner.FileIndexOfEpisode(files, 2));
        Assert.Null(EpisodePlanner.FileIndexOfEpisode(files, 3));
        Assert.Null(EpisodePlanner.FileIndexOfEpisode(files, null));
    }

    [Fact]
    public void Channel_ItemIds_RoundTrip()
    {
        var id = Guid.NewGuid();
        Assert.True(RuTrackerChannel.TryParseFile(RuTrackerChannel.FileId(id, 12), out var parsed, out var index));
        Assert.Equal(id, parsed);
        Assert.Equal(12, index);

        Assert.True(RuTrackerChannel.TryParseFolder("d" + id.ToString("N"), out var folder));
        Assert.Equal(id, folder);
        Assert.False(RuTrackerChannel.TryParseFolder(null, out _));
        Assert.False(RuTrackerChannel.TryParseFile("f-1", out _, out _));
    }

    [Fact]
    public void ForumClassifier_DocumentaryMiscForum_IsShow()
    {
        Assert.Equal(MediaKind.Show, ForumClassifier.Classify(2176, "[Док] Разное / некондиция"));
        Assert.Equal(MediaKind.Show, ForumClassifier.ClassifyByName("[Док] Что-то новое"));
    }

    private sealed class ManualClock : TimeProvider
    {
        public ManualClock(DateTimeOffset now) => Now = now;

        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
