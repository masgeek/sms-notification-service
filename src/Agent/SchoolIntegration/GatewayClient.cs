using System.Net;
using System.Diagnostics;
using System.Buffers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed class GatewayClient(HttpClient httpClient, Microsoft.Extensions.Options.IOptions<AgentOptions> options)
{
    private const int MaxGatewayErrorLength = 2048;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };
    private readonly AgentOptions _options = options.Value;

    public string? LastRequestId { get; private set; }

    public async Task<SyncWork?> LeaseAsync(int waitSeconds, CancellationToken cancellationToken)
    {
        var boundedWait = Math.Clamp(waitSeconds, 0, 55);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await httpClient.GetAsync($"{_options.AgentWorkEndpoint}?wait={boundedWait}", cancellationToken);
            CaptureRequestId(response);
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            await EnsureSuccessAsync(response, cancellationToken);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<SyncWork>>(JsonOptions, cancellationToken);
            return envelope?.Data ?? throw new InvalidOperationException("Lease response did not contain work data.");
        }
        finally
        {
            AgentMetrics.RecordLease(stopwatch.Elapsed);
        }
    }

    public async Task<AgentHeartbeatResponse> HeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(_options.AgentHeartbeatEndpoint, heartbeat, JsonOptions, cancellationToken);
        CaptureRequestId(response);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<AgentHeartbeatResponse>>(JsonOptions, cancellationToken);
        return envelope?.Data ?? throw new InvalidOperationException("Heartbeat response did not contain configuration data.");
    }

    public async Task RenewLeaseAsync(SyncWork work, CancellationToken cancellationToken)
    {
        using var request = CreateLeasedRequest(HttpMethod.Post, Format(_options.AgentRenewEndpoint, work.JobId), work);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        CaptureRequestId(response);
        await EnsureSuccessAsync(response, cancellationToken, leaseMutation: true);
    }

    public async Task UploadPageAsync(
        SyncWork work,
        int pageNumber,
        CanonicalPage page,
        CancellationToken cancellationToken)
    {
        using var request = CreateLeasedRequest(HttpMethod.Put, Format(_options.AgentPageEndpoint, work.JobId, pageNumber), work);
        request.Content = CreatePageContent(page);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        CaptureRequestId(response);
        await EnsureSuccessAsync(response, cancellationToken, leaseMutation: true);
    }

    public async Task<CompletionResult> CompleteAsync(SyncWork work, CompletionManifest manifest, CancellationToken cancellationToken)
    {
        using var request = CreateLeasedRequest(HttpMethod.Post, Format(_options.AgentCompleteEndpoint, work.JobId), work);
        request.Content = JsonContent.Create(manifest, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        CaptureRequestId(response);
        await EnsureSuccessAsync(response, cancellationToken, leaseMutation: true);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CompletionResult>>(JsonOptions, cancellationToken);
        return envelope?.Data ?? throw new InvalidOperationException("Completion response did not contain status data.");
    }

    public async Task ReportExpectedRecordCountAsync(SyncWork work, int expectedRecordCount, CancellationToken cancellationToken)
    {
        using var request = CreateLeasedRequest(HttpMethod.Post, Format(_options.AgentProgressEndpoint, work.JobId), work);
        request.Content = JsonContent.Create(new { expected_record_count = expectedRecordCount }, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        CaptureRequestId(response);
        await EnsureSuccessAsync(response, cancellationToken, leaseMutation: true);
    }

    public async Task CompletePaymentAsync(SyncWork work, PaymentDeliveryResult result, CancellationToken cancellationToken)
    {
        using var request = CreateLeasedRequest(HttpMethod.Post, Format(_options.AgentPaymentCompleteEndpoint, work.JobId), work);
        request.Content = JsonContent.Create(result, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        CaptureRequestId(response);
        await EnsureSuccessAsync(response, cancellationToken, leaseMutation: true);
    }

    public async Task FailAsync(SyncWork work, string failureCode, CancellationToken cancellationToken)
    {
        using var request = CreateLeasedRequest(HttpMethod.Post, Format(_options.AgentFailEndpoint, work.JobId), work);
        request.Content = JsonContent.Create(new { failure_code = failureCode }, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        CaptureRequestId(response);
        await EnsureSuccessAsync(response, cancellationToken, leaseMutation: true);
    }

    public static string HashPage(IReadOnlyList<StudentRecordV1> records)
    {
        return HashRecords(records);
    }

    public static string HashRecords<T>(IReadOnlyList<T> records)
    {
        return SerializePage(records).ContentHash;
    }

    public static CanonicalPage SerializePage<T>(IReadOnlyList<T> records)
    {
        var recordsJson = JsonSerializer.SerializeToUtf8Bytes(records, JsonOptions);
        return new CanonicalPage(Convert.ToHexStringLower(SHA256.HashData(recordsJson)), recordsJson);
    }

    private static HttpRequestMessage CreateLeasedRequest(HttpMethod method, string url, SyncWork work)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Lease-Token", work.LeaseToken);
        request.Headers.Add("X-Lease-Generation", work.LeaseGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return request;
    }

    private static HttpContent CreatePageContent(CanonicalPage page)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("content_hash", page.ContentHash);
            writer.WritePropertyName("records");
            writer.WriteRawValue(page.RecordsJson, skipInputValidation: false);
            writer.WriteEndObject();
        }

        var content = new ByteArrayContent(buffer.WrittenSpan.ToArray());
        content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
        return content;
    }

    private static string Format(string template, string jobId, int? pageNumber = null) =>
        template.Replace("{jobId}", Uri.EscapeDataString(jobId), StringComparison.Ordinal)
            .Replace("{pageNumber}", (pageNumber ?? 0).ToString(), StringComparison.Ordinal);

    private void CaptureRequestId(HttpResponseMessage response)
    {
        LastRequestId = response.Headers.TryGetValues("X-Request-Id", out var values)
            ? values.FirstOrDefault()
            : null;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        bool leaseMutation = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            if (leaseMutation)
            {
                throw new AgentLeaseLostException("The server rejected stale lease credentials with HTTP 401.");
            }

            throw new AgentAuthenticationException("The agent credential was rejected; re-enrollment is required.");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new AgentAuthenticationException("The agent credential does not have permission for this operation.");
        }

        if (leaseMutation && response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionRequired)
        {
            throw new AgentLeaseLostException($"The server rejected lease credentials with HTTP {(int)response.StatusCode}.");
        }

        if (leaseMutation && response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new AgentLeaseLostException("The leased job is no longer available.");
        }

        if (!leaseMutation && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            throw new AgentProtocolException($"The configured agent endpoint rejected the protocol with HTTP {(int)response.StatusCode}.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta
                ?? response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow
                ?? TimeSpan.FromSeconds(30);
            throw new AgentRateLimitException(retryAfter);
        }

        if ((int)response.StatusCode is >= 400 and < 500 && response.StatusCode != HttpStatusCode.RequestTimeout)
        {
            var gatewayError = await ReadGatewayErrorAsync(response, cancellationToken);
            var summary = $"Agent gateway rejected the request payload with HTTP {(int)response.StatusCode} ({response.StatusCode}). Code={gatewayError.Code}.";
            if (!string.IsNullOrWhiteSpace(gatewayError.Reason))
            {
                summary += $" Reason: {gatewayError.Reason}";
            }

            throw new AgentRequestRejectedException(gatewayError.Code, summary);
        }

        throw new HttpRequestException($"HTTP {(int)response.StatusCode} ({response.StatusCode}) from agent gateway.");
    }

    private static async Task<GatewayErrorDetails> ReadGatewayErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var error = TryGetObject(root, "error");
            var code = NormalizeFailureCode(
                GetString(root, "code", "error_code")
                ?? (error is { } errorObject ? GetString(errorObject, "code", "error_code") : null));
            var reasons = new List<string>();
            AddReason(reasons, GetString(root, "message", "reason", "detail"));
            if (error is { } nestedError)
            {
                AddReason(reasons, GetString(nestedError, "message", "reason", "detail"));
            }

            if (TryGetProperty(root, out var errors, "errors", "validation_errors"))
            {
                AddValidationErrors(errors, reasons);
            }
            else if (error is { } errorDetails
                     && TryGetProperty(errorDetails, out errors, "errors", "validation_errors"))
            {
                AddValidationErrors(errors, reasons);
            }

            var reason = string.Join("; ", reasons.Distinct(StringComparer.Ordinal));
            if (reason.Length > MaxGatewayErrorLength)
            {
                reason = reason[..MaxGatewayErrorLength] + " [truncated]";
            }

            return new GatewayErrorDetails(code, reason);
        }
        catch (JsonException)
        {
            return new GatewayErrorDetails("INVALID_PAYLOAD", string.Empty);
        }
    }

    private static void AddValidationErrors(JsonElement errors, List<string> reasons, string? path = null)
    {
        if (errors.ValueKind == JsonValueKind.Object)
        {
            var field = GetString(errors, "field", "path");
            var message = GetString(errors, "message", "reason", "detail");
            if (!string.IsNullOrWhiteSpace(message))
            {
                AddReason(reasons, string.IsNullOrWhiteSpace(field) ? message : $"{field}: {message}");
                return;
            }

            foreach (var property in errors.EnumerateObject())
            {
                AddValidationErrors(property.Value, reasons, CombinePath(path, property.Name));
            }
            return;
        }

        if (errors.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in errors.EnumerateArray())
            {
                AddValidationErrors(item, reasons, path);
            }
            return;
        }

        if (errors.ValueKind == JsonValueKind.String)
        {
            var message = errors.GetString();
            AddReason(reasons, string.IsNullOrWhiteSpace(path) ? message : $"{path}: {message}");
        }
    }

    private static string CombinePath(string? path, string segment) =>
        string.IsNullOrWhiteSpace(path) ? segment : $"{path}.{segment}";

    private static void AddReason(List<string> reasons, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        var normalized = reason.Replace('\r', ' ').Replace('\n', ' ').Trim();
        reasons.Add(normalized.Length > 512 ? normalized[..512] + " [truncated]" : normalized);
    }

    private static string NormalizeFailureCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64
            || code.Any(character => !char.IsLetterOrDigit(character) && character is not '_' and not '-' and not '.'))
        {
            return "INVALID_PAYLOAD";
        }

        return code.ToUpperInvariant();
    }

    private static JsonElement? TryGetObject(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static string? GetString(JsonElement element, params string[] names) =>
        TryGetProperty(element, out var value, names) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out value))
                {
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private sealed record GatewayErrorDetails(string Code, string Reason);
}

internal sealed class AgentAuthenticationException(string message) : Exception(message);

internal sealed class AgentLeaseLostException(string message) : Exception(message);

internal sealed class AgentRateLimitException(TimeSpan retryAfter) : Exception("The agent gateway rate limit was reached.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

internal sealed class AgentRequestRejectedException(string failureCode, string message) : Exception(message)
{
    public string FailureCode { get; } = failureCode;
}

internal sealed class AgentProtocolException(string message) : Exception(message);
