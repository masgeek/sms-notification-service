using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FeeSyncer.Shared;

public enum FeeProcessorDiagnosticEndpoint
{
    Login,
    StudentCount,
    FeeCount,
    StudentsFirstPage,
    FeesFirstPage,
}

public sealed class FeeProcessorDiagnosticClient(HttpClient httpClient, string username, string password)
{
    private const int DiagnosticPageSize = 3;
    private const int MaxDiagnosticBodyLength = 64 * 1024;
    private readonly bool includeResponseDetails;

    public FeeProcessorDiagnosticClient(HttpClient httpClient, string username, string password, bool includeResponseDetails)
        : this(httpClient, username, password)
    {
        this.includeResponseDetails = includeResponseDetails;
    }

    public async Task<CheckResult> CheckAsync(
        FeeProcessorDiagnosticEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        if (httpClient.BaseAddress is null || !httpClient.BaseAddress.IsLoopback)
        {
            return Failed("Local API URL must use localhost or a loopback IP address");
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return Failed("Local API username and password are required");
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var token = await LoginAsync(cancellationToken);
            if (endpoint == FeeProcessorDiagnosticEndpoint.Login)
            {
                return Passed("Authentication succeeded", stopwatch);
            }

            return endpoint switch
            {
                FeeProcessorDiagnosticEndpoint.StudentCount => await CheckCountAsync(
                    "v1/students/count", "Student count", token, stopwatch, cancellationToken),
                FeeProcessorDiagnosticEndpoint.FeeCount => await CheckCountAsync(
                    "v1/fees/count", "Fee count", token, stopwatch, cancellationToken),
                FeeProcessorDiagnosticEndpoint.StudentsFirstPage => await CheckFirstPageAsync(
                    "v1/students", "Students", token, stopwatch, cancellationToken),
                FeeProcessorDiagnosticEndpoint.FeesFirstPage => await CheckFirstPageAsync(
                    "v1/fees", "Fees", token, stopwatch, cancellationToken),
                _ => Failed("Unsupported diagnostic endpoint"),
            };
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed($"Request timed out after {httpClient.Timeout.TotalSeconds:0} seconds");
        }
        catch (HttpRequestException exception)
        {
            return Failed(exception.StatusCode is null
                ? "Local API could not be reached"
                : $"Local API returned HTTP {(int)exception.StatusCode} ({exception.StatusCode})");
        }
        catch (DiagnosticResponseException exception)
        {
            return Failed(exception.Message);
        }
        catch (JsonException)
        {
            return Failed("Local API returned invalid JSON");
        }
    }

    private async Task<string> LoginAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "v1/users/login",
            new { username, password },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("token", out var tokenElement)
            || string.IsNullOrWhiteSpace(tokenElement.GetString()))
        {
            throw new JsonException("Login response did not contain a token.");
        }

        return tokenElement.GetString()!;
    }

    private async Task<CheckResult> CheckCountAsync(
        string path,
        string label,
        string token,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        using var response = await SendAuthenticatedGetAsync(path, token, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken, token);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var countElement = root.TryGetProperty("count", out var rootCount)
            ? rootCount
            : root.TryGetProperty("data", out var data)
              && data.ValueKind == JsonValueKind.Object
              && data.TryGetProperty("count", out var nestedCount)
                ? nestedCount
                : default;

        if (countElement.ValueKind != JsonValueKind.Number
            || !countElement.TryGetInt32(out var count)
            || count < 0)
        {
            return Failed($"{label} response did not contain a valid count");
        }

        return Passed($"{label} endpoint returned {count:N0} records", stopwatch);
    }

    private async Task<CheckResult> CheckFirstPageAsync(
        string path,
        string label,
        string token,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        using var response = await SendAuthenticatedGetAsync(
            $"{path}?page=1&per_page={DiagnosticPageSize}", token, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken, token);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var records)
            || records.ValueKind != JsonValueKind.Array)
        {
            return Failed($"{label} response did not contain a data array");
        }

        var hasNextPage = document.RootElement.TryGetProperty("next_page_url", out var nextPage)
            && nextPage.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(nextPage.GetString());
        var recordCount = records.GetArrayLength();
        var recordLabel = recordCount == 1 ? "record" : "records";
        var pagination = hasNextPage ? "; another page is available" : "; this is the final page";

        return Passed($"{label} endpoint returned a valid first page with {recordCount:N0} {recordLabel}{pagination}", stopwatch);
    }

    private async Task<HttpResponseMessage> SendAuthenticatedGetAsync(
        string path,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await httpClient.SendAsync(request, cancellationToken);
    }

    private async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        params string[] additionalSecrets)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var summary = $"Local API returned HTTP {(int)response.StatusCode} ({response.StatusCode})";
        if (!includeResponseDetails)
        {
            throw new DiagnosticResponseException(summary);
        }

        var headers = response.Headers
            .Concat(response.Content.Headers)
            .Select(header => $"{header.Key}: {(IsSensitiveHeader(header.Key) ? "[redacted]" : string.Join(", ", header.Value))}");
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        body = Redact(body, username);
        body = Redact(body, password);
        foreach (var secret in additionalSecrets)
        {
            body = Redact(body, secret);
        }
        if (body.Length > MaxDiagnosticBodyLength)
        {
            body = body[..MaxDiagnosticBodyLength] + $"{Environment.NewLine}[truncated]";
        }

        throw new DiagnosticResponseException(
            $"{summary}{Environment.NewLine}Response headers:{Environment.NewLine}{string.Join(Environment.NewLine, headers)}{Environment.NewLine}Response body:{Environment.NewLine}{(body.Length == 0 ? "[empty]" : body)}");
    }

    private static bool IsSensitiveHeader(string name) =>
        name.Contains("authorization", StringComparison.OrdinalIgnoreCase)
        || name.Contains("cookie", StringComparison.OrdinalIgnoreCase)
        || name.Contains("token", StringComparison.OrdinalIgnoreCase)
        || name.Contains("key", StringComparison.OrdinalIgnoreCase);

    private static string Redact(string value, string secret) =>
        string.IsNullOrEmpty(secret) ? value : value.Replace(secret, "[redacted]", StringComparison.Ordinal);

    private static CheckResult Passed(string details, Stopwatch stopwatch)
    {
        stopwatch.Stop();

        return new CheckResult
        {
            Passed = true,
            ResponseTime = stopwatch.ElapsedMilliseconds,
            Details = $"{details} ({stopwatch.ElapsedMilliseconds}ms)",
        };
    }

    private static CheckResult Failed(string details) => new() { Passed = false, Details = details };

    private sealed class DiagnosticResponseException(string message) : Exception(message);
}
