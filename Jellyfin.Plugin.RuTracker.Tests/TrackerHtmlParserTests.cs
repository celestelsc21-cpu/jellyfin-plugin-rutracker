using System;
using System.Linq;
using Jellyfin.Plugin.RuTracker.Configuration;
using Jellyfin.Plugin.RuTracker.Tracker;
using Xunit;

namespace Jellyfin.Plugin.RuTracker.Tests;

public class TrackerHtmlParserTests
{
    // Trimmed copy of the tracker.php result table layout (columns 1-10).
    private const string SearchPage = """
        <html><body>
        <a id="logged-in-username" href="profile.php">me</a>
        <table class="forumline tablesorter" id="tor-tbl">
          <thead><tr><th>x</th></tr></thead>
          <tbody>
            <tr id="trs-tr-6543210" class="tCenter hl-tr" data-topic_id="6543210">
              <td class="row1 t-ico"></td>
              <td class="row1 t-ico" title="проверено"></td>
              <td class="row1 f-name-col"><div class="f-name"><a class="gen f ts-text" href="tracker.php?f=7">Зарубежное кино</a></div></td>
              <td class="row4 med tLeft t-title-col tt"><div class="wbr t-title"><a data-topic_id="6543210" class="med tLink tt-text ts-text hl-tags bold" href="viewtopic.php?t=6543210">Дюна: Часть вторая / Dune: Part Two
                (Дени Вильнёв) [2024, WEB-DL 1080p]</a></div></td>
              <td class="row1 u-name-col"><div class="wbr u-name"><a class="med ts-text" href="tracker.php?pid=1">Uploader</a></div></td>
              <td class="row4 small nowrap tor-size" data-ts_text="15032385536"><a class="small tr-dl dl-stub" href="dl.php?t=6543210">14 GB</a></td>
              <td class="row4 nowrap" data-ts_text="1234"><b class="seedmed">1234</b></td>
              <td class="row4 leechmed bold" title="Личи">56</td>
              <td class="row4 small number-format">9 876</td>
              <td class="row4 small nowrap" data-ts_text="1714000000"><p>25-Апр-24</p></td>
            </tr>
            <tr class="tCenter hl-tr">
              <td></td><td></td>
              <td class="row1 f-name-col"><div class="f-name"><a href="tracker.php?f=189">Зарубежные сериалы</a></div></td>
              <td class="t-title-col"><div class="t-title"><a class="tLink" href="viewtopic.php?t=777">Сериал (Сезон 1)</a></div></td>
              <td class="u-name-col">Someone</td>
              <td class="tor-size" data-ts_text="1000"></td>
              <td class="row4 nowrap"><span>4 дн</span></td>
              <td>0</td>
              <td>3</td>
              <td data-ts_text="0"></td>
            </tr>
            <tr class="tCenter hl-tr">
              <td></td><td></td>
              <td class="f-name-col"><div class="f-name"><a href="tracker.php?f=999999">Рок</a></div></td>
              <td class="t-title-col"><div class="t-title"><a class="tLink" href="viewtopic.php?t=888">Альбом</a></div></td>
              <td class="u-name-col">X</td><td class="tor-size" data-ts_text="10"></td><td><b>1</b></td><td>0</td><td>0</td><td></td>
            </tr>
            <tr><td colspan="10" class="row1 tCenter pad_8">Не найдено</td></tr>
          </tbody>
        </table>
        </body></html>
        """;

    [Fact]
    public void ParsesFullRow()
    {
        var first = TrackerHtmlParser.ParseSearchResults(SearchPage)[0];

        Assert.Equal(6543210, first.TopicId);
        Assert.Equal("Дюна: Часть вторая / Dune: Part Two (Дени Вильнёв) [2024, WEB-DL 1080p]", first.Title);
        Assert.Equal(7, first.ForumId);
        Assert.Equal("Зарубежное кино", first.ForumName);
        Assert.Equal(MediaKind.Movie, first.Kind);
        Assert.Equal(15032385536, first.SizeBytes);
        Assert.Equal(1234, first.Seeders);
        Assert.Equal(56, first.Leechers);
        Assert.Equal(9876, first.Downloads);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1714000000), first.Added);
        Assert.Equal("Uploader", first.Author);
    }

    [Fact]
    public void HandlesMissingSeedsAndFallbackIds()
    {
        var second = TrackerHtmlParser.ParseSearchResults(SearchPage)[1];

        Assert.Equal(777, second.TopicId); // from href, no data-topic_id
        Assert.Equal(MediaKind.Series, second.Kind);
        Assert.Equal(0, second.Seeders); // "4 дн" = no seeders for 4 days
        Assert.Null(second.Added);
    }

    [Fact]
    public void KeepsNonVideoRowsWithoutKind_AndSkipsEmptyRow()
    {
        var all = TrackerHtmlParser.ParseSearchResults(SearchPage);

        Assert.Equal(3, all.Count);
        Assert.Null(all.Single(t => t.TopicId == 888).Kind);
    }

    [Fact]
    public void EmptyOrForeignPage_ReturnsNothing()
    {
        Assert.Empty(TrackerHtmlParser.ParseSearchResults("<html><body>login</body></html>"));
        Assert.Empty(TrackerHtmlParser.ParseSearchResults(string.Empty));
    }

    [Fact]
    public void DetectsLoginStateAndCaptcha()
    {
        Assert.True(TrackerHtmlParser.IsLoggedIn(SearchPage));
        Assert.False(TrackerHtmlParser.IsLoggedIn("<form action=\"login.php\">"));
        Assert.True(TrackerHtmlParser.HasCaptcha("<input type=\"hidden\" name=\"cap_sid\" value=\"abc\">"));
        Assert.False(TrackerHtmlParser.HasCaptcha(SearchPage));
    }
}
