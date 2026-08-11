using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed class GatewayClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string? LastRequestId { get; private set; }

    public async Task<SyncWork?> LeaseAsync(int waitSeconds, CancellationToken cancellationToken)
    {
        var boundedWait = Math.Clamp(waitSeconds, 0, 55);
        using var response = await httpClient.GetAsync($"api/agent/work?wait={boundedWait}", cancellationToken);
        CaptureRequestId(response);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<SyncWork>>(JsonOptions, cancellationToken);
        return envelope?.Data ?? throw new InvalidOperationException("Lease response did not contain work data.");
    }

    public async Task HeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("api/agent/heartbeat", heartbeat, JsonOptions, cancellationToken);
        CaptureRequestId(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task UploadPageAsync(SyncWork work, int pageNumber, object records, string hash, CancellationToken cancellationToken)
    {
        using var request = CreateLeasedRequest(HttpMethod.Put, $"api/agent/sync-jobs/{work.JobId}/pages/{pageNumber}", work.LeaseToken);
        request.Content = JsonContent.Create(new PageUpload(hash, records), options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        CaptureRequestId(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task CompleteAsync(SyncWork work, CompletionManifest manifest, CancellationToken cancellationToken)
    {
        using var request = CreateLeasedRequest(HttpMethod.Post, $"api/agent/sync-jobs/{work.JobId}/complete", work.LeaseToken);
        request.Content = JsonContent.Create(manifest, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        CaptureRequestId(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task CompletePaymentAsync(SyncWork work, PaymentDeliveryResult result, CancellationToken cancellationToken)
    {
        using var request = CreateLeasedRequest(HttpMethod.Post, $"api/agent/payment-jobs/{work.JobId}/complete", work.LeaseToken);
        request.Content = JsonContent.Create(result, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        CaptureRequestId(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task FailAsync(SyncWork work, string failureCode, CancellationToken cancellationToken)
    {
        using var request = CreateLeasedRequest(HttpMethod.Post, $"api/agent/sync-jobs/{work.JobId}/fail", work.LeaseToken);
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

    private void CaptureRequestId(HttpResponseMessage response)
    {
        LastRequestId = response.Headers.TryGetValues("X-Request-Id", out var values)
            ? values.FirstOrDefault()
            : null;
    }
}
