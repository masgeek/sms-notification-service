using System.Text.Json.Serialization;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed record ApiEnvelope<T>([property: JsonPropertyName("data")] T Data);

internal sealed record AgentHeartbeat(
    [property: JsonPropertyName("agent_version")] string AgentVersion,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<string> Capabilities,
    [property: JsonPropertyName("adapter_versions")] IReadOnlyList<string> AdapterVersions);

internal sealed record SyncWork(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("parameters")] SyncParameters Parameters,
    [property: JsonPropertyName("lease_token")] string LeaseToken,
    [property: JsonPropertyName("lease_expires_at")] DateTimeOffset LeaseExpiresAt,
    [property: JsonPropertyName("confirmed_pages")] Dictionary<int, string> ConfirmedPages);

internal sealed record SyncParameters(
    [property: JsonPropertyName("page_size")] int PageSize = 100,
    [property: JsonPropertyName("payment")] PaymentRequestV1? Payment = null);

internal sealed record PaymentRequestV1(
    [property: JsonPropertyName("student_reg")] string StudentReg,
    [property: JsonPropertyName("mpesa_code")] string MpesaCode,
    [property: JsonPropertyName("dated")] string Dated,
    [property: JsonPropertyName("pay_description")] string? PayDescription,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("amount")] string Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("transaction_type")] string TransactionType,
    [property: JsonPropertyName("account_no")] string? AccountNo,
    [property: JsonPropertyName("mobile_no")] string? MobileNo,
    [property: JsonPropertyName("payment_info")] string? PaymentInfo,
    [property: JsonPropertyName("student_names")] string? StudentNames,
    [property: JsonPropertyName("post_date")] string? PostDate);

internal sealed record PaymentDeliveryResult(
    [property: JsonPropertyName("status")] string Status);

internal sealed record FeeRecordV1
{
    [JsonPropertyName("source_student_id"), JsonPropertyOrder(1)]
    public required string SourceStudentId { get; init; }

    [JsonPropertyName("amount_due"), JsonPropertyOrder(2)]
    public string AmountDue { get; init; } = "0.00";

    [JsonPropertyName("balance"), JsonPropertyOrder(3)]
    public string Balance { get; init; } = "0.00";

    [JsonPropertyName("opening_balance"), JsonPropertyOrder(4)]
    public string OpeningBalance { get; init; } = "0.00";

    [JsonPropertyName("currency"), JsonPropertyOrder(5)]
    public string Currency { get; init; } = "KES";

    [JsonPropertyName("source_updated_at"), JsonPropertyOrder(6)]
    public string? SourceUpdatedAt { get; init; }

    [JsonPropertyName("name"), JsonPropertyOrder(7)]
    public string? Name { get; init; }

    [JsonPropertyName("parent_name"), JsonPropertyOrder(8)]
    public string? ParentName { get; init; }

    [JsonPropertyName("house"), JsonPropertyOrder(9)]
    public string? House { get; init; }

    [JsonPropertyName("year"), JsonPropertyOrder(10)]
    public string? Year { get; init; }

    [JsonPropertyName("form"), JsonPropertyOrder(11)]
    public string? Form { get; init; }

    [JsonPropertyName("term"), JsonPropertyOrder(12)]
    public string? Term { get; init; }

    [JsonPropertyName("phone"), JsonPropertyOrder(13)]
    public string? Phone { get; init; }
}

internal sealed record StudentRecordV1
{
    [JsonPropertyName("admission_number"), JsonPropertyOrder(1)]
    public string? AdmissionNumber { get; init; }

    [JsonPropertyName("class_identifier"), JsonPropertyOrder(2)]
    public string? ClassIdentifier { get; init; }

    [JsonPropertyName("enrollment_status"), JsonPropertyOrder(3)]
    public required string EnrollmentStatus { get; init; }

    [JsonPropertyName("source_student_id"), JsonPropertyOrder(4)]
    public required string SourceStudentId { get; init; }

    [JsonPropertyName("source_updated_at"), JsonPropertyOrder(5)]
    public string? SourceUpdatedAt { get; init; }

    [JsonPropertyName("name"), JsonPropertyOrder(6)]
    public string? Name { get; init; }

    [JsonPropertyName("phone"), JsonPropertyOrder(7)]
    public string? Phone { get; init; }

    [JsonPropertyName("stream"), JsonPropertyOrder(8)]
    public string? Stream { get; init; }

    [JsonPropertyName("form"), JsonPropertyOrder(9)]
    public string? Form { get; init; }

    [JsonPropertyName("term"), JsonPropertyOrder(10)]
    public string? Term { get; init; }

    [JsonPropertyName("year"), JsonPropertyOrder(11)]
    public string? Year { get; init; }

    [JsonPropertyName("parent_name"), JsonPropertyOrder(12)]
    public string? ParentName { get; init; }

    [JsonPropertyName("balance"), JsonPropertyOrder(13)]
    public string Balance { get; init; } = "0.00";

    [JsonPropertyName("class_number"), JsonPropertyOrder(14)]
    public string? ClassNumber { get; init; }
}

internal sealed record PageUpload(
    [property: JsonPropertyName("content_hash")] string ContentHash,
    [property: JsonPropertyName("records")] object Records);

internal sealed record CompletionManifest(
    [property: JsonPropertyName("page_hashes")] IReadOnlyList<string> PageHashes,
    [property: JsonPropertyName("record_count")] int RecordCount,
    [property: JsonPropertyName("checkpoint")] Dictionary<string, string> Checkpoint);
