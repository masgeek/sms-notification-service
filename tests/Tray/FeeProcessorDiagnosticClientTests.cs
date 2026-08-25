using System.Net;
using System.Net.Http;
using System.Text;
using FeeSyncer.Shared;
using Xunit;

namespace FeeSyncer.Tray.Tests;

public sealed class FeeProcessorDiagnosticClientTests
{
    [Theory]
    [InlineData(FeeProcessorDiagnosticEndpoint.StudentCount, "/api/v1/students/count", "{\"count\":20000}", "20,000 records")]
    [InlineData(FeeProcessorDiagnosticEndpoint.FeeCount, "/api/v1/fees/count", "{\"data\":{\"count\":15000}}", "15,000 records")]
    [InlineData(FeeProcessorDiagnosticEndpoint.StudentsFirstPage, "/api/v1/students", "{\"data\":[{\"admno\":\"PRIVATE-ADM\",\"name\":\"Private Student\"}],\"next_page_url\":\"http://localhost/api/v1/students?page=2\"}", "1 record; another page is available")]
    [InlineData(FeeProcessorDiagnosticEndpoint.FeesFirstPage, "/api/v1/fees", "{\"data\":[{\"admno\":\"PRIVATE-ADM\",\"balance\":9000}],\"next_page_url\":null}", "1 record; this is the final page")]
    public async Task Authenticates_and_validates_endpoint_without_returning_record_data(
        FeeProcessorDiagnosticEndpoint endpoint,
        string expectedPath,
        string responseJson,
        string expectedSummary)
    {
        var requests = new List<CapturedRequest>();
        using var http = new HttpClient(new StubHandler(request =>
        {
            requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.RequestUri.Query,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));

            return request.RequestUri.AbsolutePath.EndsWith("/v1/users/login", StringComparison.Ordinal)
                ? JsonResponse("{\"token\":\"private-token\"}")
                : JsonResponse(responseJson);
        }))
        {
            BaseAddress = new Uri("http://127.0.0.1:8001/api/"),
        };
        var client = new FeeProcessorDiagnosticClient(http, "private-user", "private-password");

        var result = await client.CheckAsync(endpoint);

        Assert.True(result.Passed, result.Details);
        Assert.Contains(expectedSummary, result.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE-ADM", result.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("Private Student", result.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("private-token", result.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("private-password", result.Details, StringComparison.Ordinal);
        Assert.Equal("/api/v1/users/login", requests[0].Path);
        Assert.Equal(expectedPath, requests[1].Path);
        Assert.Equal("Bearer", requests[1].AuthorizationScheme);
        Assert.Equal("private-token", requests[1].AuthorizationParameter);
        if (endpoint is FeeProcessorDiagnosticEndpoint.StudentsFirstPage or FeeProcessorDiagnosticEndpoint.FeesFirstPage)
        {
            Assert.Equal("?page=1&per_page=3", requests[1].Query);
        }
    }

    [Fact]
    public async Task Rejects_non_loopback_urls_before_sending_credentials()
    {
        var requests = 0;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            requests++;
            return JsonResponse("{}");
        }))
        {
            BaseAddress = new Uri("https://fees.example.test/api/"),
        };
        var client = new FeeProcessorDiagnosticClient(http, "private-user", "private-password");

        var result = await client.CheckAsync(FeeProcessorDiagnosticEndpoint.Login);

        Assert.False(result.Passed);
        Assert.Contains("loopback", result.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task Rejects_a_successful_login_without_a_token()
    {
        using var http = new HttpClient(new StubHandler(_ => JsonResponse("{\"success\":true}")))
        {
            BaseAddress = new Uri("http://localhost:8001/api/"),
        };
        var client = new FeeProcessorDiagnosticClient(http, "private-user", "private-password");

        var result = await client.CheckAsync(FeeProcessorDiagnosticEndpoint.Login);

        Assert.False(result.Passed);
        Assert.Equal("Local API returned invalid JSON", result.Details);
    }

    [Fact]
    public async Task Debug_failure_includes_response_details_without_credentials()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(
                "Server exception for private-user using private-password",
                Encoding.UTF8,
                "text/plain"),
        }))
        {
            BaseAddress = new Uri("http://127.0.0.1:8001/api/"),
        };
        var client = new FeeProcessorDiagnosticClient(http, "private-user", "private-password", includeResponseDetails: true);

        var result = await client.CheckAsync(FeeProcessorDiagnosticEndpoint.Login);

        Assert.False(result.Passed);
        Assert.Contains("HTTP 500", result.Details, StringComparison.Ordinal);
        Assert.Contains("Response headers:", result.Details, StringComparison.Ordinal);
        Assert.Contains("Response body:", result.Details, StringComparison.Ordinal);
        Assert.Contains("Server exception", result.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("private-user", result.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("private-password", result.Details, StringComparison.Ordinal);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(handler(request));
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Path,
        string Query,
        string? AuthorizationScheme,
        string? AuthorizationParameter);
}
