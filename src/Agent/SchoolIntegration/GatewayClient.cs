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
        int? expectedRecordCount,
        CancellationToken cancellationToken)
    {
        using var request = CreateLeasedRequest(HttpMethod.Put, Format(_options.AgentPageEndpoint, work.JobId, pageNumber), work);
        request.Content = CreatePageContent(page, expectedRecordCount);
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

    private static HttpContent CreatePageContent(CanonicalPage page, int? expectedRecordCount)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("content_hash", page.ContentHash);
            if (expectedRecordCount is not null)
            {
                writer.WriteNumber("expected_record_count", expectedRecordCount.Value);
            }
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

    private static Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        bool leaseMutation = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (response.IsSuccessStatusCode)
        {
            return Task.CompletedTask;
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
            throw new AgentRequestRejectedException("INVALID_PAYLOAD");
        }

        throw new HttpRequestException($"HTTP {(int)response.StatusCode} ({response.StatusCode}) from agent gateway.");
    }
}

internal sealed class AgentAuthenticationException(string message) : Exception(message);

internal sealed class AgentLeaseLostException(string message) : Exception(message);

internal sealed class AgentRateLimitException(TimeSpan retryAfter) : Exception("The agent gateway rate limit was reached.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

internal sealed class AgentRequestRejectedException(string failureCode) : Exception("The agent gateway rejected the request payload.")
{
    public string FailureCode { get; } = failureCode;
}

internal sealed class AgentProtocolException(string message) : Exception(message);
