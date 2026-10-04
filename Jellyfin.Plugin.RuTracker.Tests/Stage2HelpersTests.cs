using System.Linq;
using Jellyfin.Plugin.RuTracker.Configuration;
using Jellyfin.Plugin.RuTracker.Tracker;
using Jellyfin.Plugin.RuTracker.Web;
using Xunit;

namespace Jellyfin.Plugin.RuTracker.Tests;

public class Stage2HelpersTests
{
    [Theory]
    [InlineData("Дюна: Часть вторая", "Дюна Часть вторая")]
    [InlineData("  breaking   bad!!! s01 ", "breaking bad s01")]
    [InlineData("ab", "ab")]
    [InlineData("a", null)]
    [InlineData("!!!", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void SearchQuery_Normalize(string? input, string? expected)
        => Assert.Equal(expected, SearchQuery.Normalize(input));

    [Fact]
    public void SearchQuery_IsTruncated()
        => Assert.Equal(SearchQuery.MaxLength, SearchQuery.Normalize(new string('я', 500))!.Length);

    [Theory]
    [InlineData("abcdef1234567890", "abcdef1234567890")]
    [InlineData("bb_session=abcdef1234567890;", "abcdef1234567890")]
    [InlineData(" BB_SESSION=0-12345-AbCdEf== ", "0-12345-AbCdEf==")]
    [InlineData("abc\r\nX-Evil: 1", null)] // header injection
    [InlineData("short", null)]
    [InlineData("", null)]
    public void SessionCookie_Normalize(string input, string? expected)
        => Assert.Equal(expected, SessionCookie.Normalize(input));

    [Fact]
    public void SessionCookie_FromSetCookie()
    {
        Assert.Equal(
            "0-1234567-AbCdEfGh",
            SessionCookie.FromSetCookie(["bb_ssl=1; path=/", "bb_session=0-1234567-AbCdEfGh; expires=Tue, 01-Jan-2030 00:00:00 GMT; path=/forum/; HttpOnly"]));
        Assert.Null(SessionCookie.FromSetCookie(["bb_session=deleted; expires=Thu, 01-Jan-1970 00:00:01 GMT"]));
        Assert.Null(SessionCookie.FromSetCookie(["other=1"]));
    }

    [Theory]
    [InlineData(7, null, MediaKind.Movie)] // Зарубежное кино
    [InlineData(189, null, MediaKind.Series)] // Зарубежные сериалы
    [InlineData(24, null, MediaKind.Show)] // Развлекательные телепередачи и шоу
    [InlineData(1, "Новый раздел: Сериалы Кореи", MediaKind.Series)]
    [InlineData(1, "Новинки кино 2026", MediaKind.Movie)]
    [InlineData(1, "Телепередачи и шоу", MediaKind.Show)]
    public void ForumClassifier_Classify(int forumId, string? forumName, MediaKind expected)
        => Assert.Equal(expected, ForumClassifier.Classify(forumId, forumName));

    [Theory]
    [InlineData(1, "Рок")]
    [InlineData(1, null)]
    public void ForumClassifier_NonVideo_IsNull(int forumId, string? forumName)
        => Assert.Null(ForumClassifier.Classify(forumId, forumName));

    [Fact]
    public void ForumMap_ContainsAllThreeKinds()
    {
        var kinds = ForumMap.Kinds.Values.Distinct().ToList();

        Assert.Contains(MediaKind.Movie, kinds);
        Assert.Contains(MediaKind.Series, kinds);
        Assert.Contains(MediaKind.Show, kinds);
    }

    [Fact]
    public void HtmlInjector_InsertsBeforeBodyEnd_Once()
    {
        var tag = HtmlInjector.ScriptTag("0.2.0.0");
        var html = "<html><body><div id=\"app\"></div></BODY></html>";

        var once = HtmlInjector.Inject(html, tag);
        var twice = HtmlInjector.Inject(once, tag);

        Assert.Equal("<html><body><div id=\"app\"></div>" + tag + "</BODY></html>", once);
        Assert.Equal(once, twice);
        Assert.Contains("../RuTracker/Web/header.js?v=0.2.0.0", tag, System.StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlInjector_WithoutBody_LeavesPageUntouched()
    {
        const string Fragment = "<div>no body here</div>";

        Assert.Same(Fragment, HtmlInjector.Inject(Fragment, HtmlInjector.ScriptTag("1")));
    }
}
