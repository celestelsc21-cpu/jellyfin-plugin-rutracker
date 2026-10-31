using System.Collections.Generic;
using Jellyfin.Plugin.RuTracker.Tracker;
using Xunit;

namespace Jellyfin.Plugin.RuTracker.Tests;

public class CookieJarTests
{
    [Fact]
    public void CombinesCookiesIntoOneHeader()
    {
        var cookies = new Dictionary<string, string>
        {
            ["cf_clearance"] = "test-clearance",
            ["bb_session"] = "test-session",
        };

        Assert.Equal("cf_clearance=test-clearance; bb_session=test-session", CookieJar.ToHeader(cookies));
    }

    [Fact]
    public void OmitsSetCookieAttributes()
    {
        var cookies = CookieJar.Collect(["bb_session=test-session; Path=/; HttpOnly", "service=test; Secure"]);
        Assert.Equal("bb_session=test-session; service=test", CookieJar.ToHeader(cookies));
    }
}
