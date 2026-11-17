using System.Collections.Generic;
using Jellyfin.Plugin.RuTracker.Library;
using Jellyfin.Plugin.RuTracker.Torrents;
using Xunit;

namespace Jellyfin.Plugin.RuTracker.Tests;

public class EpisodeFileNamingTests
{
    [Theory]
    [InlineData("01 серия_Как приручить лису", 1, 1, 1)]
    [InlineData("Серия 02", 1, 1, 2)]
    [InlineData("Show.S01E03", 1, 1, 3)]
    [InlineData("Show.1x04", 1, 1, 4)]
    [InlineData("Show.S02E04", 1, 0, 0)]
    [InlineData("Episode_05", 1, 0, 0)]
    public void TryParse_RecognizesOnlyExplicitEpisodeNumbers(string name, int defaultSeason, int expectedSeason, int expectedEpisode)
    {
        var found = EpisodeFileNaming.TryParse(name, defaultSeason, out var season, out var episode);
        Assert.Equal(expectedEpisode > 0, found);
        if (found)
        {
            Assert.Equal(expectedSeason, season);
            Assert.Equal(expectedEpisode, episode);
        }
    }

    [Fact]
    public void Plan_KeepsIndexAndParentFolder()
    {
        var files = new List<TorrentFileEntry>
        {
            new(2, "Season/01 серия_Как приручить лису.mkv", 100),
            new(5, "Season/02 серия_Как приручить лису.mkv", 100)
        };

        var result = EpisodeFileNaming.Plan(files, 1, "Как приручить лису");
        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Index);
        Assert.Equal("Season/Как приручить лису - S01E01.mkv", result[0].NewPath);
        Assert.Equal(5, result[1].Index);
        Assert.Equal("Season/Как приручить лису - S01E02.mkv", result[1].NewPath);
    }

    [Fact]
    public void Plan_DoesNotOverwriteExistingFiles()
    {
        var files = new List<TorrentFileEntry>
        {
            new(0, "01 серия.mkv", 100),
            new(1, "Show - S01E01.mkv", 100)
        };

        var result = EpisodeFileNaming.Plan(files, 1, "Show");
        Assert.Empty(result);
    }

    [Fact]
    public void Plan_DoesNotGuessWithoutSelectedSeason()
    {
        var files = new List<TorrentFileEntry>
        {
            new(0, "01 серия.mkv", 100)
        };

        Assert.Empty(EpisodeFileNaming.Plan(files, null, "Show"));
    }
}
