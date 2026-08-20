using System.Net;
using System.Net.Http;
using System.Text;
using FeeSyncer.Shared;
using FluentAssertions;

namespace FeeSyncer.Tray.Tests;

public sealed class UpdateCheckerFallbackTests
{
    [Fact]
    public async Task CheckAsync_PrimaryUnavailable_UsesGitHubReleaseManifest()
    {
        var requestedHosts = new List<string>();
        using var http = new HttpClient(new StubHandler(request =>
        {
            requestedHosts.Add(request.RequestUri!.Host);
            return request.RequestUri.Host == "s3.munywele.co.ke"
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : JsonResponse(CreateManifest(
                    "https://github.com/masgeek/sms-notification-service/releases/download/v99.0.0/FeeSyncer-Setup-99.0.0.exe"));
        }));
        using var checker = new UpdateChecker(http);

        var result = await checker.CheckAsync(notifyAvailable: false);

        result.Succeeded.Should().BeTrue();
        result.LatestVersion.Should().Be("99.0.0");
        result.DownloadUrl.Should().StartWith("https://github.com/masgeek/");
        requestedHosts.Should().Equal("s3.munywele.co.ke", "github.com");
    }

    [Fact]
    public async Task CheckAsync_PrimaryValid_DoesNotContactFallback()
    {
        var requestedHosts = new List<string>();
        using var http = new HttpClient(new StubHandler(request =>
        {
            requestedHosts.Add(request.RequestUri!.Host);
            return JsonResponse(CreateManifest(
                "https://s3.munywele.co.ke/fee-syncer/99.0.0/FeeSyncer-Setup-99.0.0.exe"));
        }));
        using var checker = new UpdateChecker(http);

        var result = await checker.CheckAsync(notifyAvailable: false);

        result.Succeeded.Should().BeTrue();
        result.DownloadUrl.Should().StartWith("https://s3.munywele.co.ke/");
        requestedHosts.Should().Equal("s3.munywele.co.ke");
    }

    private static string CreateManifest(string installerUrl) => $$"""
        {
          "version": "99.0.0",
          "publishedAt": "2026-08-20T00:00:00Z",
          "installers": {
            "selfContained": {
              "url": "{{installerUrl}}",
              "sha256": "{{new string('A', 64)}}",
              "size": 123
            }
          }
        }
        """;

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
