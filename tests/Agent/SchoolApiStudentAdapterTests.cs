using Microsoft.Extensions.Options;
using FeeSyncer.Agent.SchoolIntegration;
using Xunit;

namespace FeeSyncer.Agent.Tests;

public sealed class SchoolApiStudentAdapterTests
{
    [Fact]
    public async Task Reads_students_from_the_loopback_school_api()
    {
        var loginAttempts = 0;
        using var httpClient = new HttpClient(new StubHandler(request => Task.FromResult(new HttpResponseMessage
        {
            StatusCode = System.Net.HttpStatusCode.OK,
            Content = new StringContent(request.RequestUri?.AbsolutePath.EndsWith("/v1/users/login", StringComparison.Ordinal) == true
                ? LoginResponse(ref loginAttempts)
                : request.RequestUri?.AbsolutePath.EndsWith("/v1/students/count", StringComparison.Ordinal) == true
                    ? "{\"count\":42}"
                    : "{\"data\":[{\"admno\":\"SYN-001\",\"ClassNo\":\"FORM-1-A\"}],\"next_page_url\":null}"),
        })))
        {
            BaseAddress = new Uri("http://127.0.0.1:8080/api/"),
        };
        var client = new SchoolApiClient(httpClient, Options.Create(new AgentOptions
        {
            LocalApiUsername = "synthetic-user",
            LocalApiPassword = "synthetic-password",
        }));
        var adapter = new SchoolApiStudentAdapter(client);
        var expectedCount = await client.GetExpectedStudentCountAsync(CancellationToken.None);

        var records = new List<StudentRecordV1>();
        await foreach (var record in adapter.ReadSnapshotAsync(CancellationToken.None))
        {
            records.Add(record);
        }

        var student = Assert.Single(records);
        Assert.Equal("SYN-001", student.SourceStudentId);
        Assert.Equal("FORM-1-A", student.ClassIdentifier);
        Assert.Equal(42, expectedCount);
        Assert.Equal(1, loginAttempts);
    }

    [Fact]
    public async Task Refreshes_the_local_token_before_expiry_and_follows_next_page_url()
    {
        var loginAttempts = 0;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/v1/users/login", StringComparison.Ordinal) == true)
            {
                loginAttempts++;
                return Task.FromResult(JsonResponse($"{{\"token\":\"synthetic-token-{loginAttempts}\",\"expires\":1}}"));
            }

            var page = request.RequestUri?.Query.Contains("page=2", StringComparison.Ordinal) == true ? "2" : "1";
            var next = page == "1" ? "\"next_page_url\":\"http://127.0.0.1:8080/api/v1/students?page=2\"" : "\"next_page_url\":null";
            return Task.FromResult(JsonResponse($"{{\"current_page\":{page},\"data\":[{{\"admno\":\"SYN-{page}\"}}],\"last_page\":100,\"per_page\":200,\"total\":20000,{next}}}"));
        }))
        {
            BaseAddress = new Uri("http://127.0.0.1:8080/api/"),
        };
        var client = new SchoolApiClient(httpClient, Options.Create(new AgentOptions
        {
            LocalApiUsername = "synthetic-user",
            LocalApiPassword = "synthetic-password",
        }));

        var students = new List<StudentRecordV1>();
        await foreach (var student in client.ReadStudentsAsync(1, CancellationToken.None))
        {
            students.Add(student);
        }

        Assert.Equal(2, students.Count);
        Assert.Equal(2, loginAttempts);
    }

    [Fact]
    public async Task Maps_fee_balance_amounts_to_fixed_precision_KES_records()
    {
        var requestedPaths = new List<string>();
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            requestedPaths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);

            return Task.FromResult(JsonResponse(request.RequestUri?.AbsolutePath.EndsWith("/v1/users/login", StringComparison.Ordinal) == true
                ? "{\"token\":\"synthetic-local-token\",\"expires\":900}"
                : request.RequestUri?.AbsolutePath.EndsWith("/v1/fees/count", StringComparison.Ordinal) == true
                    ? "{\"data\":{\"count\":1}}"
                    : "{\"data\":[{\"admno\":\"SYN-001\",\"Payable\":59000,\"Bal\":-7800,\"Opening_Balance\":40000,\"Dated\":\"2026-05-06\",\"Name\":\"Synthetic Student\",\"phone\":\"0700000000\"}],\"next_page_url\":null}"));
        }))
        {
            BaseAddress = new Uri("http://127.0.0.1:8001/api/"),
        };
        var client = new SchoolApiClient(httpClient, Options.Create(new AgentOptions
        {
            LocalApiUsername = "synthetic-user",
            LocalApiPassword = "synthetic-password",
        }));

        var expectedCount = await client.GetExpectedFeeCountAsync(CancellationToken.None);
        var fees = new List<FeeRecordV1>();
        await foreach (var record in client.ReadFeeBalancesAsync(2, CancellationToken.None))
        {
            fees.Add(record);
        }
        var fee = Assert.Single(fees);

        Assert.Equal("SYN-001", fee.SourceStudentId);
        Assert.Equal("59000.00", fee.AmountDue);
        Assert.Equal("-7800.00", fee.Balance);
        Assert.Equal("40000.00", fee.OpeningBalance);
        Assert.Equal("KES", fee.Currency);
        Assert.Equal("Synthetic Student", fee.Name);
        Assert.Equal(1, expectedCount);
        Assert.Contains("/api/v1/fees", requestedPaths);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handler(request);
        }
    }

    private static string LoginResponse(ref int loginAttempts)
    {
        loginAttempts++;
        return $$"""{"token":"synthetic-local-token","expiresAt":"{{DateTimeOffset.UtcNow.AddMinutes(15):O}}","expires":899.722721,"expiresInHuman":"14 minutes 59 seconds"}""";
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
    }
}
