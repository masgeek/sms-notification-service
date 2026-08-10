using Microsoft.Extensions.Options;
using SmsNotificationService.SchoolIntegration;
using Xunit;

namespace SmsNotificationService.SchoolIntegration.Tests;

public sealed class SchoolApiStudentAdapterTests
{
    [Fact]
    public async Task Reads_students_from_the_loopback_school_api()
    {
        using var httpClient = new HttpClient(new StubHandler(request => Task.FromResult(new HttpResponseMessage
        {
            StatusCode = System.Net.HttpStatusCode.OK,
            Content = new StringContent(request.RequestUri?.AbsolutePath.EndsWith("/v1/users/login", StringComparison.Ordinal) == true
                ? "{\"token\":\"synthetic-local-token\"}"
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

        var records = new List<StudentRecordV1>();
        await foreach (var record in adapter.ReadSnapshotAsync(CancellationToken.None))
        {
            records.Add(record);
        }

        var student = Assert.Single(records);
        Assert.Equal("SYN-001", student.SourceStudentId);
        Assert.Equal("FORM-1-A", student.ClassIdentifier);
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
            return Task.FromResult(JsonResponse($"{{\"data\":[{{\"admno\":\"SYN-{page}\"}}],{next}}}"));
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
        using var httpClient = new HttpClient(new StubHandler(request => Task.FromResult(JsonResponse(
            request.RequestUri?.AbsolutePath.EndsWith("/v1/users/login", StringComparison.Ordinal) == true
                ? "{\"token\":\"synthetic-local-token\",\"expires\":600000}"
                : "{\"data\":[{\"admno\":\"SYN-001\",\"Payable\":59000,\"Bal\":-7800,\"Opening_Balance\":40000,\"Dated\":\"2026-05-06\",\"Name\":\"Synthetic Student\",\"phone\":\"0700000000\"}],\"next_page_url\":null}"))))
        {
            BaseAddress = new Uri("http://127.0.0.1:8001/api/"),
        };
        var client = new SchoolApiClient(httpClient, Options.Create(new AgentOptions
        {
            LocalApiUsername = "synthetic-user",
            LocalApiPassword = "synthetic-password",
        }));

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
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handler(request);
        }
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
    }
}
