using System.Net;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed class GatewayClient(HttpClient httpClient, Microsoft.Extensions.Options.IOptions<AgentOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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

            response.EnsureSuccessStatusCode();
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<SyncWork>>(JsonOptions, cancellationToken);
            return envelope?.Data ?? throw new InvalidOperationException("Lease response did not contain work data.");
        }
        finally
        {
            AgentMetrics.RecordLease(stopwatch.Elapsed);
        }
    }

    public async Task HeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(_options.AgentHeartbeatEndpoint, heartbeat, JsonOptions, cancellationToken);
        CaptureRequestId(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task RenewLeaseAsync(SyncWork work, CancellationToken cancellationToken)
    {
        using var request = CreateLeasedRequest(HttpMethod.Post, Format(_options.AgentRenewEndpoint, work.JobId), work.LeaseToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        CaptureRequestId(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task UploadPageAsync(SyncWork work, int pageNumber, object records, string hash, CancellationToken cancellationToken)
    {
        using var request = CreateLeasedRequest(HttpMethod.Put, Format(_options.AgentPageEndpoint, work.JobId, pageNumber), work.LeaseToken);
        request.Content = JsonContent.Create(new PageUpload(hash, records), options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        CaptureRequestId(response);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task CompleteAsync(SyncWork work, CompletionManifest manifest, CancellationToken cancellationToken)
    {
        using var request = CreateLeasedRequest(HttpMethod.Post, Format(_options.AgentCompleteEndpoint, work.JobId), work.LeaseToken);
        request.Content = JsonContent.Create(manifest, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        CaptureRequestId(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task CompletePaymentAsync(SyncWork work, PaymentDeliveryResult result, CancellationToken cancellationToken)
    {
        using var request = CreateLeasedRequest(HttpMethod.Post, Format(_options.AgentPaymentCompleteEndpoint, work.JobId), work.LeaseToken);
        request.Content = JsonContent.Create(result, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        CaptureRequestId(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task FailAsync(SyncWork work, string failureCode, CancellationToken cancellationToken)
    {
        using var request = CreateLeasedRequest(HttpMethod.Post, Format(_options.AgentFailEndpoint, work.JobId), work.LeaseToken);
        request.Content = JsonContent.Create(new { failure_code = failureCode }, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        CaptureRequestId(response);
        response.EnsureSuccessStatusCode();
    }

    public static string HashPage(IReadOnlyList<StudentRecordV1> records)
    {
        return HashRecords(records);
    }

    public static string HashRecords<T>(IReadOnlyList<T> records)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(records, JsonOptions);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static HttpRequestMessage CreateLeasedRequest(HttpMethod method, string url, string leaseToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Lease-Token", leaseToken);
        return request;
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

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        body = body.Length > 2_000 ? body[..2_000] : body;
        throw new HttpRequestException($"HTTP {(int)response.StatusCode} ({response.StatusCode}) from agent gateway: {body}");
    }
}
