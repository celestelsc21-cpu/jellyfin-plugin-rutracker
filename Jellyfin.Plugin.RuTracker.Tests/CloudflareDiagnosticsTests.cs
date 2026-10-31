using System.Net;
using System.Net.Http;
using Jellyfin.Plugin.RuTracker.Tracker;
using Xunit;

namespace Jellyfin.Plugin.RuTracker.Tests;

public class CloudflareDiagnosticsTests
{
    [Fact]
    public void RecognizesMitigationHeader()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.Add("cf-mitigated", "challenge");
        var result = CloudflareDiagnostics.Detect(response, string.Empty);
        Assert.NotNull(result);
        Assert.Contains("HTTP 403", result);
        Assert.Contains("cf-mitigated: challenge", result);
    }

    [Fact]
    public void RecognizesChallengeHtml()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        var result = CloudflareDiagnostics.Detect(response, "<html><script src=\"/cf-chl-example\"></script></html>");
        Assert.NotNull(result);
        Assert.Contains("JavaScript/Managed Challenge", result);
    }

    [Fact]
    public void DoesNotClassifyUnrelatedForbiddenResponse()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        Assert.Null(CloudflareDiagnostics.Detect(response, "<html>Access denied</html>"));
    }
}
