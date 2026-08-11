using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    private sealed record HashFixture(
        [property: JsonPropertyName("records")] StudentRecordV1[] Records,
        [property: JsonPropertyName("expected_page_hash")] string ExpectedPageHash);

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
