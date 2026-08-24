using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed class SchoolApiClient(HttpClient httpClient, IOptions<AgentOptions> options)
{
    private const string DuplicateMessage = "The mpesa code has already been taken.";
    private string? accessToken;
    private DateTimeOffset accessTokenExpiresAt = DateTimeOffset.MinValue;

    private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromSeconds(30);

    public async Task<PaymentDeliveryResult> RecordPaymentAsync(
        PaymentRequestV1 payment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.LocalApiUsername)
            || string.IsNullOrWhiteSpace(options.Value.LocalApiPassword))
        {
            throw new SchoolApiException("LOCAL_CONFIGURATION_INVALID");
        }

        await EnsureTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/payments")
        {
            Content = JsonContent.Create(payment),
        };
        request.Headers.Authorization = new("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return new PaymentDeliveryResult("accepted");
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            accessToken = null;
            accessTokenExpiresAt = DateTimeOffset.MinValue;
            throw new SchoolApiException("LOCAL_AUTH_FAILED");
        }

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            if (await IsDuplicateAsync(response, cancellationToken))
            {
                return new PaymentDeliveryResult("duplicate");
            }

            throw new SchoolApiException("PAYMENT_REJECTED");
        }

        if ((int)response.StatusCode == 408 || response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
        {
            throw new SchoolApiException("LOCAL_UNAVAILABLE");
        }

        throw new SchoolApiException("PAYMENT_REJECTED");
    }

    public async IAsyncEnumerable<StudentRecordV1> ReadStudentsAsync(
        int pageSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in ReadRecordsAsync("v1/students", pageSize, cancellationToken))
        {
            var sourceId = StringValue(item, "admno", "adm_no");
            if (sourceId is null)
            {
                // Ignore malformed source rows rather than creating an unkeyed balance.
                continue;
            }

            yield return new StudentRecordV1
            {
                SourceStudentId = sourceId,
                AdmissionNumber = sourceId,
                EnrollmentStatus = "active",
                ClassIdentifier = StringValue(item, "ClassNo", "class_number", "form"),
                SourceUpdatedAt = StringValue(item, "updated_at"),
                Name = StringValue(item, "Name", "name") ?? sourceId,
                Phone = StringValue(item, "phone", "phone_number"),
                Stream = StringValue(item, "STREAM", "stream"),
                Form = StringValue(item, "form"),
                Term = StringValue(item, "term"),
                Year = StringValue(item, "year"),
                ParentName = StringValue(item, "pname", "parent_name"),
                Balance = DecimalValue(item, "Balance", "balance"),
                ClassNumber = StringValue(item, "ClassNo", "class_number"),
            };
        }
    }

    public async IAsyncEnumerable<FeeRecordV1> ReadFeeBalancesAsync(
        int pageSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in ReadRecordsAsync("v1/fees", pageSize, cancellationToken))
        {
            var sourceId = StringValue(item, "admno", "adm_no");
            if (sourceId is null)
            {
                continue;
            }

            yield return new FeeRecordV1
            {
                SourceStudentId = sourceId,
                AmountDue = DecimalValue(item, "Payable", "payable"),
                Balance = DecimalValue(item, "Bal", "balance"),
                OpeningBalance = DecimalValue(item, "Opening_Balance", "opening_balance"),
                Currency = "KES",
                SourceUpdatedAt = StringValue(item, "updated_at", "Dated", "dated"),
                Name = StringValue(item, "Name", "name") ?? sourceId,
                ParentName = StringValue(item, "pname", "parent_name"),
                House = StringValue(item, "HOUSE", "house"),
                Year = StringValue(item, "year"),
                Form = StringValue(item, "form"),
                Term = StringValue(item, "term"),
                Phone = StringValue(item, "phone", "phone_number"),
            };
        }
    }

    public Task<int> GetExpectedStudentCountAsync(CancellationToken cancellationToken) =>
        ReadExpectedCountAsync("v1/students/count", cancellationToken);

    public Task<int> GetExpectedFeeCountAsync(CancellationToken cancellationToken) =>
        ReadExpectedCountAsync("v1/fees/count", cancellationToken);

    private async Task EnsureTokenAsync(CancellationToken cancellationToken)
    {
        if (accessToken is not null && DateTimeOffset.UtcNow < accessTokenExpiresAt - TokenRefreshSkew)
        {
            return;
        }

        var login = await LoginAsync(cancellationToken);
        accessToken = login.Token;

        var expiresByDuration = login.Expires > 0
            ? DateTimeOffset.UtcNow.AddMilliseconds(login.Expires)
            : DateTimeOffset.MaxValue;
        var expiresAt = login.ExpiresAt ?? DateTimeOffset.MaxValue;
        accessTokenExpiresAt = expiresByDuration < expiresAt ? expiresByDuration : expiresAt;
    }

    private async Task<LoginResponse> LoginAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "v1/users/login",
            new
            {
                username = options.Value.LocalApiUsername,
                password = options.Value.LocalApiPassword,
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new SchoolApiException(
                response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? "LOCAL_AUTH_FAILED"
                    : "LOCAL_UNAVAILABLE");
        }

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken);
        if (login?.Token is null || login.Token.Length == 0)
        {
            throw new SchoolApiException("LOCAL_AUTH_FAILED");
        }

        return login;
    }

    private async IAsyncEnumerable<JsonElement> ReadRecordsAsync(
        string path,
        int pageSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var page = 1; ; page++)
        {
            await EnsureTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{path}?page={page}&per_page={pageSize}");
            request.Headers.Authorization = new("Bearer", accessToken);
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                accessToken = null;
                accessTokenExpiresAt = DateTimeOffset.MinValue;
                throw new SchoolApiException("LOCAL_AUTH_FAILED");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new SchoolApiException("LOCAL_UNAVAILABLE");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("data", out var records) || records.ValueKind != JsonValueKind.Array)
            {
                throw new SchoolApiException("SYNC_FAILED");
            }

            var hasNextPage = document.RootElement.TryGetProperty("next_page_url", out var next)
                && next.ValueKind is not JsonValueKind.Null
                && !string.IsNullOrWhiteSpace(next.GetString());

            foreach (var record in records.EnumerateArray())
            {
                yield return record.Clone();
            }

            if (records.GetArrayLength() == 0 || !hasNextPage)
            {
                yield break;
            }
        }
    }

    private async Task<int> ReadExpectedCountAsync(string path, CancellationToken cancellationToken)
    {
        await EnsureTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            accessToken = null;
            accessTokenExpiresAt = DateTimeOffset.MinValue;
            throw new SchoolApiException("LOCAL_AUTH_FAILED");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new SchoolApiException("LOCAL_UNAVAILABLE");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var count = root.TryGetProperty("count", out var rootCount)
            ? rootCount
            : root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("count", out var nestedCount)
                ? nestedCount
                : default;
        if (count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var result) || result < 0)
        {
            throw new SchoolApiException("SYNC_FAILED");
        }

        return result;
    }

    private static string? StringValue(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
            {
                return value.ToString();
            }
        }

        return null;
    }

    private static string DecimalValue(JsonElement element, params string[] names)
    {
        var value = StringValue(element, names);
        return decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount)
            ? amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            : "0.00";
    }

    private static async Task<bool> IsDuplicateAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("mpesa_code", out var errors)
            || errors.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return errors.EnumerateArray().Any(error => error.GetString() == DuplicateMessage);
    }

    private sealed record LoginResponse(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("expiresAt")] DateTimeOffset? ExpiresAt = null,
        [property: JsonPropertyName("expires")] double Expires = 0);
}

internal sealed class SchoolApiException(string failureCode) : Exception
{
    public string FailureCode { get; } = failureCode;
}
