using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using FeeSyncer.Agent.SchoolIntegration;
using Xunit;

namespace FeeSyncer.Agent.Tests;

public sealed class StudentSyncContractTests
{
    [Fact]
    public void Hash_is_stable_for_the_versioned_wire_contract()
    {
        StudentRecordV1[] records = [new()
        {
            SourceStudentId = "synthetic-00001",
            AdmissionNumber = "SYN-001",
            EnrollmentStatus = "active",
            ClassIdentifier = "FORM-1-A",
            SourceUpdatedAt = "2026-08-09T00:00:00Z",
        }];

        var first = GatewayClient.HashPage(records);
        var second = GatewayClient.HashPage(records);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.DoesNotContain(first, character => !Uri.IsHexDigit(character));
    }

    [Fact]
    public void Hash_matches_the_shared_php_contract_fixture()
    {
        var fixture = JsonSerializer.Deserialize<HashFixture>(
            File.ReadAllText(Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                 "../../../../contracts/student-record-v1-fixture.json"))),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(fixture);
        Assert.Equal(fixture.ExpectedPageHash, GatewayClient.HashPage(fixture.Records));
    }

    [Fact]
    public void Hash_preserves_unicode_names_for_the_php_contract()
    {
        StudentRecordV1[] records = [new()
        {
            SourceStudentId = "SYN-001",
            AdmissionNumber = "SYN-001",
            EnrollmentStatus = "active",
            Name = "Achieng Odhiambo / 阿香",
        }];

        var page = GatewayClient.SerializePage(records);
        var json = Encoding.UTF8.GetString(page.RecordsJson);

        Assert.Contains("阿香", json, StringComparison.Ordinal);
        Assert.Contains(" / ", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u963f", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(page.RecordsJson)), page.ContentHash);
    }

    [Fact]
    public void Wire_contract_uses_allowlisted_snake_case_fields_only()
    {
        var record = new StudentRecordV1 { SourceStudentId = "student-001", EnrollmentStatus = "active" };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(record, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

        Assert.Equal(
            new[]
            {
                "admission_number", "class_identifier", "enrollment_status", "source_student_id", "source_updated_at",
                "name", "phone", "stream", "form", "term", "year", "parent_name", "balance", "class_number",
            },
            names);
    }

    [Fact]
    public void Debug_sample_redacts_student_identity_and_financial_values()
    {
        StudentRecordV1[] records = [new()
        {
            SourceStudentId = "private-source-id",
            AdmissionNumber = "private-admission",
            EnrollmentStatus = "active",
            ClassIdentifier = "FORM-1-A",
            Name = "Private Student",
            Phone = "254700000000",
            ParentName = "Private Parent",
            Balance = "1234.56",
        }];

        var sample = SchoolIntegrationWorker.CreateRedactedStudentSamples(records);

        Assert.Contains("FORM-1-A", sample, StringComparison.Ordinal);
        Assert.Contains("active", sample, StringComparison.Ordinal);
        Assert.Contains("redacted", sample, StringComparison.Ordinal);
        Assert.DoesNotContain("private-source-id", sample, StringComparison.Ordinal);
        Assert.DoesNotContain("private-admission", sample, StringComparison.Ordinal);
        Assert.DoesNotContain("Private Student", sample, StringComparison.Ordinal);
        Assert.DoesNotContain("254700000000", sample, StringComparison.Ordinal);
        Assert.DoesNotContain("Private Parent", sample, StringComparison.Ordinal);
        Assert.DoesNotContain("1234.56", sample, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Synthetic_adapter_returns_multiple_bounded_pages()
    {
        var adapter = new SyntheticStudentAdapter();
        var students = new List<StudentRecordV1>();

        await foreach (var student in adapter.ReadSnapshotAsync(CancellationToken.None))
        {
            students.Add(student);
        }

        Assert.Equal(250, students.Count);
        Assert.All(students, record => Assert.StartsWith("synthetic-", record.SourceStudentId));
    }

    [Fact]
    public async Task School_api_client_accepts_new_and_duplicate_payments()
    {
        var payment = new PaymentRequestV1(
            "SYN-ADM-001",
            "SYN-MPESA-001",
            "2026-08-10T10:00:00Z",
            "Synthetic school fees",
            "PENDING",
            "1500.00",
            "KES",
            "FEE PAYMENT",
            "SYN-TILL-001",
            "254700000000",
            "Synthetic payment",
            "Synthetic Student",
            "2026-08-10T10:00:00Z");
        var paymentAttempts = 0;
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/v1/users/login", StringComparison.Ordinal) == true)
            {
                return JsonResponse(HttpStatusCode.OK, "{\"token\":\"synthetic-local-token\"}");
            }

            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("\"currency\":\"KES\"", body);
            paymentAttempts++;

            return paymentAttempts == 1
                ? JsonResponse(HttpStatusCode.OK, "{\"success\":true,\"data\":{\"ID\":1}}")
                : JsonResponse(HttpStatusCode.UnprocessableEntity, "{\"data\":{\"mpesa_code\":[\"The mpesa code has already been taken.\"]}}");
        }))
        {
            BaseAddress = new Uri("http://127.0.0.1:8080/api/"),
        };
        var options = Options.Create(new AgentOptions
        {
            LocalApiUsername = "synthetic-user",
            LocalApiPassword = "synthetic-password",
        });
        var schoolApi = new SchoolApiClient(httpClient, options);

        Assert.Equal("accepted", (await schoolApi.RecordPaymentAsync(payment, CancellationToken.None)).Status);
        Assert.Equal("duplicate", (await schoolApi.RecordPaymentAsync(payment, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Gateway_uses_heartbeat_configuration_and_deserializes_lease_generation()
    {
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            var response = request.Method == HttpMethod.Post
                ? JsonResponse(HttpStatusCode.OK, """{"data":{"accepted":true,"work_poll_seconds":30,"long_poll_max_seconds":10,"require_lease_generation":true}}""")
                : JsonResponse(HttpStatusCode.OK, """{"data":{"job_id":"job-1","operation":"students.snapshot.v1","schema_version":1,"parameters":{"page_size":100},"lease_token":"lease-token","lease_generation":7,"lease_expires_at":"2026-08-24T10:00:00Z","confirmed_pages":{}}}""");
            return Task.FromResult(response);
        }))
        {
            BaseAddress = new Uri("https://gateway.example.test/"),
        };
        var gateway = new GatewayClient(httpClient, Options.Create(new AgentOptions()));

        var heartbeat = await gateway.HeartbeatAsync(new AgentHeartbeat("1.0.0", ["students.snapshot.v1"], ["adapter:1"]), CancellationToken.None);
        var work = await gateway.LeaseAsync(10, CancellationToken.None);

        Assert.True(heartbeat.RequireLeaseGeneration);
        Assert.Equal(30, heartbeat.WorkPollSeconds);
        Assert.Equal(10, heartbeat.LongPollMaxSeconds);
        Assert.Equal(7, work?.LeaseGeneration);
    }

    [Fact]
    public async Task Every_lease_mutation_sends_token_and_generation_headers_and_accepts_202_completion()
    {
        var requests = new List<(string Path, string Token, string Generation, string? Body)>();
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            requests.Add((
                request.RequestUri!.AbsolutePath,
                request.Headers.GetValues("X-Lease-Token").Single(),
                request.Headers.GetValues("X-Lease-Generation").Single(),
                request.Content is null ? null : await request.Content.ReadAsStringAsync()));

            return request.RequestUri.AbsolutePath.EndsWith("/complete", StringComparison.Ordinal)
                && !request.RequestUri.AbsolutePath.Contains("payment-jobs", StringComparison.Ordinal)
                ? JsonResponse(HttpStatusCode.Accepted, """{"data":{"accepted":true,"status":"uploaded","completed":false,"duplicate":false}}""")
                : JsonResponse(HttpStatusCode.OK, "{}");
        }))
        {
            BaseAddress = new Uri("https://gateway.example.test/"),
        };
        var gateway = new GatewayClient(httpClient, Options.Create(new AgentOptions()));
        var work = CreateWork();
        var page = GatewayClient.SerializePage<StudentRecordV1>([]);

        await gateway.RenewLeaseAsync(work, CancellationToken.None);
        await gateway.ReportExpectedRecordCountAsync(work, 250, CancellationToken.None);
        await gateway.UploadPageAsync(work, 1, page, CancellationToken.None);
        var completion = await gateway.CompleteAsync(work, new CompletionManifest([page.ContentHash], 0), CancellationToken.None);
        await gateway.CompletePaymentAsync(work, new PaymentDeliveryResult("accepted"), CancellationToken.None);
        await gateway.FailAsync(work, "SYNC_FAILED", CancellationToken.None);

        Assert.Equal(6, requests.Count);
        Assert.All(requests, request =>
        {
            Assert.Equal("lease-token", request.Token);
            Assert.Equal("7", request.Generation);
        });
        Assert.True(completion.Accepted);
        Assert.Equal("uploaded", completion.Status);
        Assert.False(completion.Completed);
        var uploadBody = requests.Single(request => request.Path.Contains("/pages/", StringComparison.Ordinal)).Body;
        Assert.Contains($"\"records\":{Encoding.UTF8.GetString(page.RecordsJson)}", uploadBody, StringComparison.Ordinal);
        Assert.DoesNotContain("expected_record_count", uploadBody, StringComparison.Ordinal);
        var progressBody = requests.Single(request => request.Path.EndsWith("/progress", StringComparison.Ordinal)).Body;
        Assert.Contains("\"expected_record_count\":250", progressBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.PreconditionRequired)]
    public async Task Stale_lease_responses_are_not_treated_as_retryable_mutations(HttpStatusCode statusCode)
    {
        using var httpClient = new HttpClient(new StubHandler(_ => Task.FromResult(JsonResponse(statusCode, "{}"))))
        {
            BaseAddress = new Uri("https://gateway.example.test/"),
        };
        var gateway = new GatewayClient(httpClient, Options.Create(new AgentOptions()));

        await Assert.ThrowsAsync<AgentLeaseLostException>(() => gateway.RenewLeaseAsync(CreateWork(), CancellationToken.None));
    }

    [Fact]
    public async Task Work_endpoint_unauthorized_requires_reenrollment()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => Task.FromResult(JsonResponse(HttpStatusCode.Unauthorized, "{}"))))
        {
            BaseAddress = new Uri("https://gateway.example.test/"),
        };
        var gateway = new GatewayClient(httpClient, Options.Create(new AgentOptions()));

        await Assert.ThrowsAsync<AgentAuthenticationException>(() => gateway.LeaseAsync(10, CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_page_response_is_reportable_instead_of_retried_as_a_network_failure()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.UnprocessableEntity,
            """{"code":"PAGE_VALIDATION_FAILED","message":"Page contains invalid records.","errors":{"records.14.source_student_id":["Value is required."]}}"""))))
        {
            BaseAddress = new Uri("https://gateway.example.test/"),
        };
        var gateway = new GatewayClient(httpClient, Options.Create(new AgentOptions()));

        var exception = await Assert.ThrowsAsync<AgentRequestRejectedException>(() => gateway.UploadPageAsync(
            CreateWork(),
            1,
            GatewayClient.SerializePage<StudentRecordV1>([]),
            CancellationToken.None));

        Assert.Equal("PAGE_VALIDATION_FAILED", exception.FailureCode);
        Assert.Contains("HTTP 422", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Code=PAGE_VALIDATION_FAILED", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Page contains invalid records", exception.Message, StringComparison.Ordinal);
        Assert.Contains("records.14.source_student_id: Value is required", exception.Message, StringComparison.Ordinal);
    }

    private sealed record HashFixture(
        [property: JsonPropertyName("records")] StudentRecordV1[] Records,
        [property: JsonPropertyName("expected_page_hash")] string ExpectedPageHash);

    private static SyncWork CreateWork() => new(
        "job-1",
        "students.snapshot.v1",
        1,
        new SyncParameters(),
        "lease-token",
        7,
        DateTimeOffset.UtcNow.AddMinutes(2),
        []);

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handler(request);
        }
    }
}
