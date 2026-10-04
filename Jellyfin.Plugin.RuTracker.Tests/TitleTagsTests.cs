using Jellyfin.Plugin.RuTracker.Tracker;
using Xunit;

namespace Jellyfin.Plugin.RuTracker.Tests;

public class TitleTagsTests
{
    [Theory]
    [InlineData("Дьявол носит Prada [2006, WEB-DL 1080p] Dub + MVO + Original + Sub Rus, Eng", true)]
    [InlineData("Сериал (Сезон 1) [2024, WEB-DLRip] MVO (LostFilm) + Subs", true)]
    [InlineData("Фильм [2020] DVO + Sub (Rus)", true)]
    [InlineData("Фильм с русскими субтитрами", true)]
    [InlineData("Фильм [2020] Субтитры", true)]
    [InlineData("SUB eng", true)]
    [InlineData("Subway / Подземка [1985, BDRip] AVO", false)]
    [InlineData("The Substance / Субстанция [2024] Dub", false)]
    [InlineData("Submarine / Субмарина [2010] DVO", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasSubtitles(string? title, bool expected)
        => Assert.Equal(expected, TitleTags.HasSubtitles(title));
}
