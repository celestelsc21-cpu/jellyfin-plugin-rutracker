using System.Collections.Generic;
using Jellyfin.Plugin.RuTracker.Library;
using Xunit;

namespace Jellyfin.Plugin.RuTracker.Tests;

public class LibraryLayoutTests
{
    [Theory]
    [InlineData("Во все тяжкие / Breaking Bad / Сезон: 2 / Серии: 1-13 из 13 (Винс Гиллиган) [2009, США, WEB-DL]", 2)]
    [InlineData("Шерлок / Sherlock / Season 3 [2014]", 3)]
    [InlineData("Друзья / Friends / Сезоны: 1-10 / Серии: 1-236 из 236 [1994-2004]", null)]
    [InlineData("Сериал / Show / Сезон: 1, 2 [2020]", null)]
    [InlineData("Брат (Алексей Балабанов) [1997, Россия, драма, DVDRip]", null)]
    [InlineData("Show.S04.1080p", 4)]
    public void SeasonNumber_FromTitle(string title, int? expected)
    {
        Assert.Equal(expected, LibraryLayout.SeasonNumber(title));
    }

    [Fact]
    public void FolderTag_PrefersTmdb()
    {
        var ids = new Dictionary<string, string> { ["Imdb"] = "tt0903747", ["Tmdb"] = "1396" };
        Assert.Equal("tmdbid-1396", LibraryLayout.FolderTag(ids));
        Assert.Equal("imdbid-tt0903747", LibraryLayout.FolderTag(new Dictionary<string, string> { ["Imdb"] = "tt0903747" }));
        Assert.Null(LibraryLayout.FolderTag(new Dictionary<string, string> { ["Tmdb"] = "../x" }));
    }

    [Fact]
    public void FolderName_WithYearAndTag()
    {
        Assert.Equal("Breaking Bad (2008) [tmdbid-1396]", LibraryLayout.FolderName("Breaking Bad", 2008, "tmdbid-1396"));
        Assert.Equal("Во все тяжкие", LibraryLayout.FolderName("Во все тяжкие", null, null));
        Assert.Equal("Season 02", LibraryLayout.SeasonFolder(2));
    }

    [Fact]
    public void FindExisting_ByTagThenByName()
    {
        string[] folders = ["Breaking Bad (2009)", "Better Call Saul (2015) [tmdbid-60059]", "Тёмные (2017)"];

        Assert.Equal("Better Call Saul (2015) [tmdbid-60059]", LibraryLayout.FindExisting(folders, "tmdbid-60059", ["Лучше звоните Солу"]));
        Assert.Equal("Breaking Bad (2009)", LibraryLayout.FindExisting(folders, "tmdbid-1396", ["Breaking Bad"]));
        Assert.Equal("Тёмные (2017)", LibraryLayout.FindExisting(folders, null, ["Темные"]));
        Assert.Null(LibraryLayout.FindExisting(folders, "tmdbid-999", ["Better Call Saul"]));
        Assert.Null(LibraryLayout.FindExisting(folders, null, ["Lost"]));
    }
}
