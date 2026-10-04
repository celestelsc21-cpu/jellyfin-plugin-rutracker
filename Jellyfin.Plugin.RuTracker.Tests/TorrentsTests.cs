using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.RuTracker.Configuration;
using Jellyfin.Plugin.RuTracker.Downloads;
using Jellyfin.Plugin.RuTracker.Torrents;
using Xunit;

namespace Jellyfin.Plugin.RuTracker.Tests;

public class TorrentsTests
{
    // Multi-file torrent: "e10.mkv" is listed before "e2.mkv" on purpose, plus a subtitle file for e2.
    private const string MultiFileTorrent =
        "d8:announce12:http://x/ann4:infod5:filesld6:lengthi100e4:pathl7:e10.mkveed6:lengthi200e4:pathl6:e2.mkveed6:lengthi5e4:pathl6:e2.srteee4:name4:Show12:piece lengthi16384e6:pieces0:ee";

    [Fact]
    public void TorrentMeta_ParsesFilesAndInfoHash()
    {
        var meta = TorrentMeta.TryParse(Encoding.ASCII.GetBytes(MultiFileTorrent));

        Assert.NotNull(meta);
        Assert.Equal("ce43fc9aafd541e4712e8a4d3896330625029eed", meta!.InfoHash); // SHA-1 of the info dictionary
        Assert.Equal("Show", meta.Name);
        Assert.Equal(new[] { "e10.mkv", "e2.mkv", "e2.srt" }, meta.Files.Select(f => f.Path));
        Assert.Equal(new long[] { 100, 200, 5 }, meta.Files.Select(f => f.Length));
        Assert.Equal(new[] { 0, 1, 2 }, meta.Files.Select(f => f.Index));
    }

    [Fact]
    public void TorrentMeta_SingleFile()
    {
        var meta = TorrentMeta.TryParse(Encoding.ASCII.GetBytes("d4:infod6:lengthi42e4:name9:movie.mkv12:piece lengthi16384e6:pieces0:ee"));

        Assert.NotNull(meta);
        Assert.Single(meta!.Files);
        Assert.Equal("movie.mkv", meta.Files[0].Path);
        Assert.Equal(42, meta.Files[0].Length);
    }

    [Theory]
    [InlineData("<html><body>Ошибка</body></html>")]
    [InlineData("")]
    [InlineData("d4:infoi1ee")]
    [InlineData("d4:info")]
    [InlineData("d999999999:xe")]
    public void TorrentMeta_RejectsGarbage(string data)
        => Assert.Null(TorrentMeta.TryParse(Encoding.UTF8.GetBytes(data)));

    [Theory]
    [InlineData("magnet:?xt=urn:btih:A9993E364706816ABA3E25717850C26C9CD0D89D&tr=http%3A%2F%2Fbt.t-ru.org%2Fann", "a9993e364706816aba3e25717850c26c9cd0d89d")]
    [InlineData("magnet:?dn=x&xt=urn:btih:VGMT4NSHA2AWVOR6EVYXQUGCNSONBWE5", "a9993e364706816aba3e25717850c26c9cd0d89d")]
    [InlineData("magnet:?xt=urn:btih:short", null)]
    [InlineData("https://example.org", null)]
    [InlineData(null, null)]
    public void MagnetLink_InfoHash(string? magnet, string? expected)
        => Assert.Equal(expected, MagnetLink.GetInfoHash(magnet));

    [Fact]
    public void Planner_NaturalOrderAndVideoOnly()
    {
        var files = Files("Show/e10.mkv", "Show/e2.mkv", "Show/e2.srt", "Show/e1.mkv", "Show/readme.txt");

        Assert.Equal(new[] { "Show/e1.mkv", "Show/e2.mkv", "Show/e10.mkv" }, EpisodePlanner.VideosInOrder(files).Select(f => f.Path));
    }

    [Fact]
    public void Planner_SeasonsStayInOrder()
    {
        var files = Files("S2/e1.mkv", "S1/e2.mkv", "S1/e1.mkv", "S10/e1.mkv");

        Assert.Equal(new[] { 2, 1, 0, 3 }, EpisodePlanner.BuildOrder(files, null));
    }

    [Fact]
    public void Planner_StartFromChosenEpisode_ThenRest_ThenEarlier()
    {
        var files = Files("e1.mkv", "e2.mkv", "e3.mkv", "e4.mkv");

        Assert.Equal(new[] { 0, 1, 2, 3 }, EpisodePlanner.BuildOrder(files, null));
        Assert.Equal(new[] { 0, 1, 2, 3 }, EpisodePlanner.BuildOrder(files, 0));
        Assert.Equal(new[] { 2, 3, 0, 1 }, EpisodePlanner.BuildOrder(files, 2));
        Assert.Equal(new[] { 0, 1, 2, 3 }, EpisodePlanner.BuildOrder(files, 99)); // unknown index: from the start
    }

    [Fact]
    public void Planner_Priorities_CurrentMaximal_NextHigh_RestNormal_NothingSkipped()
    {
        var files = Files("e1.mkv", "e2.mkv", "e3.mkv", "e2.rus.mka", "e2.srt", "cover.jpg");
        var order = EpisodePlanner.BuildOrder(files, null);
        var done = new HashSet<int> { 0 }; // e1 finished

        var priorities = EpisodePlanner.Priorities(files, order, done.Contains);

        Assert.Equal(EpisodePlanner.PriorityNormal, priorities[0]); // finished
        Assert.Equal(EpisodePlanner.PriorityMaximal, priorities[1]); // e2: being watched
        Assert.Equal(EpisodePlanner.PriorityHigh, priorities[2]); // e3: next
        Assert.Equal(EpisodePlanner.PriorityMaximal, priorities[3]); // external audio of e2
        Assert.Equal(EpisodePlanner.PriorityMaximal, priorities[4]); // subtitles of e2
        Assert.Equal(EpisodePlanner.PriorityNormal, priorities[5]); // other files still download
        Assert.DoesNotContain(0, priorities.Values);
    }

    [Fact]
    public void Planner_Companions_DoNotMatchLongerEpisodeNames()
    {
        var files = Files("e1.mkv", "e10.mkv", "e10.srt");

        var priorities = EpisodePlanner.Priorities(files, EpisodePlanner.BuildOrder(files, null), _ => false);

        Assert.Equal(EpisodePlanner.PriorityMaximal, priorities[0]); // e1
        Assert.Equal(EpisodePlanner.PriorityHigh, priorities[1]); // e10
        Assert.Equal(EpisodePlanner.PriorityHigh, priorities[2]); // e10.srt follows e10, not e1
    }

    [Fact]
    public void Planner_Priorities_AllDone_AllNormal()
    {
        var files = Files("e1.mkv", "e2.mkv");

        var priorities = EpisodePlanner.Priorities(files, EpisodePlanner.BuildOrder(files, null), _ => true);

        Assert.All(priorities.Values, p => Assert.Equal(EpisodePlanner.PriorityNormal, p));
    }

    [Theory]
    [InlineData("Show/Show.S01E02.1080p.mkv", "S01E02 — Show.S01E02.1080p")]
    [InlineData("Show.s1e5.mkv", "S01E05 — Show.s1e5")]
    [InlineData("Серия 03.avi", "Серия 03")]
    public void Planner_Label(string path, string expected)
        => Assert.Equal(expected, EpisodePlanner.Label(path));

    [Fact]
    public void Messages_FollowGrammaticalGender()
    {
        Assert.Equal("Сериал «Икс» будет загружен.", DownloadMessages.WillBeDownloaded(MediaKind.Series, "Икс"));
        Assert.Equal("Фильм «Икс» будет загружен.", DownloadMessages.WillBeDownloaded(MediaKind.Movie, "Икс"));
        Assert.Equal("Передача «Икс» будет загружена.", DownloadMessages.WillBeDownloaded(MediaKind.Show, "Икс"));
    }

    private static List<TorrentFileEntry> Files(params string[] paths)
        => paths.Select((p, i) => new TorrentFileEntry(i, p, 1)).ToList();
}
