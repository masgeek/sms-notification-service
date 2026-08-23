using System.Net;
using System.Net.Http.Headers;
using FeeSyncer.Agent.SchoolIntegration;
using Microsoft.Extensions.Logging;

namespace FeeSyncer.Agent.Tests;

public sealed class AgentHttpLoggingHandlerTests
{
    [Fact]
    public async Task Debug_logging_records_exchange_without_secrets_or_bodies()
    {
        var logger = new RecordingLogger(enabled: true);
        var handler = new AgentHttpLoggingHandler(logger)
        {
            InnerHandler = new StubHandler(request =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"token\":\"response-secret\"}"),
                    RequestMessage = request,
                };
                response.Headers.Add("X-Request-Id", "request-123");
                return response;
            }),
        };
        using var http = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://gateway.example.test/api/agent/work")
        {
            Content = new StringContent("{\"password\":\"request-secret\"}"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "bearer-secret");
        request.Headers.Add("X-Lease-Token", "lease-secret");

        using var response = await http.SendAsync(request);
        var output = string.Join(Environment.NewLine, logger.Messages);

        Assert.Contains("request started", output, StringComparison.Ordinal);
        Assert.Contains("response received", output, StringComparison.Ordinal);
        Assert.Contains("StatusCode=200", output, StringComparison.Ordinal);
        Assert.Contains("RequestId=request-123", output, StringComparison.Ordinal);
        Assert.Contains("<redacted>", output, StringComparison.Ordinal);
        Assert.DoesNotContain("bearer-secret", output, StringComparison.Ordinal);
        Assert.DoesNotContain("lease-secret", output, StringComparison.Ordinal);
        Assert.DoesNotContain("request-secret", output, StringComparison.Ordinal);
        Assert.DoesNotContain("response-secret", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_debug_logging_sends_request_without_transport_logs()
    {
        var logger = new RecordingLogger(enabled: false);
        var handler = new AgentHttpLoggingHandler(logger)
        {
            InnerHandler = new StubHandler(request => new HttpResponseMessage(HttpStatusCode.NoContent)
            {
                RequestMessage = request,
            }),
        };
        using var http = new HttpClient(handler);

        using var response = await http.GetAsync("https://gateway.example.test/api/agent/work");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(logger.Messages);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class RecordingLogger(bool enabled) : ILogger<AgentHttpLoggingHandler>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => enabled && logLevel == LogLevel.Debug;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Messages.Add(formatter(state, exception));
        }
    }
}
