using System.Diagnostics;
using System.Net.Http.Headers;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed class AgentHttpLoggingHandler(ILogger<AgentHttpLoggingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!logger.IsEnabled(LogLevel.Debug))
            return await base.SendAsync(request, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        logger.LogDebug(
            "Agent HTTP request started. Method={Method} Uri={Uri} Version={Version} Headers={Headers} ContentType={ContentType} ContentLength={ContentLength}",
            request.Method,
            request.RequestUri,
            request.Version,
            FormatHeaders(request.Headers, request.Content?.Headers),
            request.Content?.Headers.ContentType?.ToString() ?? "none",
            request.Content?.Headers.ContentLength?.ToString() ?? "unknown");

        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            stopwatch.Stop();
            logger.LogDebug(
                "Agent HTTP response received. Method={Method} Uri={Uri} StatusCode={StatusCode} Reason={Reason} Version={Version} DurationMs={DurationMs} RequestId={RequestId} Headers={Headers} ContentType={ContentType} ContentLength={ContentLength}",
                request.Method,
                request.RequestUri,
                (int)response.StatusCode,
                response.ReasonPhrase,
                response.Version,
                stopwatch.ElapsedMilliseconds,
                HeaderValue(response.Headers, "X-Request-Id") ?? "none",
                FormatHeaders(response.Headers, response.Content?.Headers),
                response.Content?.Headers.ContentType?.ToString() ?? "none",
                response.Content?.Headers.ContentLength?.ToString() ?? "unknown");
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            logger.LogDebug(
                "Agent HTTP request cancelled. Method={Method} Uri={Uri} DurationMs={DurationMs}",
                request.Method,
                request.RequestUri,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            logger.LogDebug(
                exception,
                "Agent HTTP request failed. Method={Method} Uri={Uri} DurationMs={DurationMs}",
                request.Method,
                request.RequestUri,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private static string FormatHeaders(params HttpHeaders?[] groups) =>
        string.Join("; ", groups
            .Where(group => group is not null)
            .SelectMany(group => group!)
            .Select(header => $"{header.Key}={FormatHeaderValue(header.Key, header.Value)}"));

    private static string FormatHeaderValue(string name, IEnumerable<string> values)
    {
        if (IsSensitiveHeader(name))
            return "<redacted>";

        return name.Equals("Accept", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("X-Request-Id", StringComparison.OrdinalIgnoreCase)
            ? string.Join(",", values)
            : "<present>";
    }

    private static bool IsSensitiveHeader(string name) =>
        name.Contains("Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Api-Key", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Lease", StringComparison.OrdinalIgnoreCase);

    private static string? HeaderValue(HttpResponseHeaders headers, string name) =>
        headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
