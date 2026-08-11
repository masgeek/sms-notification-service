using Microsoft.Extensions.Options;

namespace SmsNotificationService.SchoolIntegration;

internal sealed class SchoolIntegrationWorker(
    GatewayClient gateway,
    IStudentAdapter adapter,
    SchoolApiClient schoolApi,
    AgentWakeSignal wakeSignal,
    IOptions<AgentOptions> options,
    ILogger<SchoolIntegrationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextHeartbeat = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            SyncWork? work = null;

            try
            {
                if (DateTimeOffset.UtcNow >= nextHeartbeat)
                {
                    await gateway.HeartbeatAsync(new AgentHeartbeat(
                        "1.0.0",
                        ["students.snapshot.v1", "fees.snapshot.v1", "payments.record.v1"],
                        [$"{adapter.Id}:{adapter.Version}"]), stoppingToken);
                    nextHeartbeat = DateTimeOffset.UtcNow.AddSeconds(options.Value.HeartbeatSeconds);
                }

                work = await gateway.LeaseAsync(options.Value.MqttEnabled ? 0 : options.Value.LongPollSeconds, stoppingToken);
                if (work is null)
                {
                    await wakeSignal.WaitAsync(TimeSpan.FromSeconds(options.Value.IdleDelaySeconds), stoppingToken);
                    continue;
                }

                await ExecuteWorkAsync(work, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Agent loop failed for job {JobId}; no student payload was logged. CorrelationId={CorrelationId}",
                    work?.JobId ?? "none",
                    gateway.LastRequestId ?? "none");

                if (work is not null)
                {
                    try
                    {
                        var failureCode = exception is SchoolApiException schoolApiException
                            ? schoolApiException.FailureCode
                            : "SYNC_FAILED";
                        await gateway.FailAsync(work, failureCode, stoppingToken);
                    }
                    catch (Exception failureException)
                    {
                        logger.LogError(failureException, "Could not report failure for job {JobId}.", work.JobId);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(options.Value.IdleDelaySeconds), stoppingToken);
            }
        }
    }

    private async Task ExecuteWorkAsync(SyncWork work, CancellationToken cancellationToken)
    {
        if (work.SchemaVersion != 1)
        {
            await gateway.FailAsync(work, "UNSUPPORTED_OPERATION", cancellationToken);
            return;
        }

        if (work.Operation == "payments.record.v1")
        {
            if (work.Parameters.Payment is null)
            {
                await gateway.FailAsync(work, "PAYMENT_REJECTED", cancellationToken);
                return;
            }

            var result = await schoolApi.RecordPaymentAsync(work.Parameters.Payment, cancellationToken);
            await gateway.CompletePaymentAsync(work, result, cancellationToken);
            logger.LogInformation("Completed payment job {JobId} with status {Status}.", work.JobId, result.Status);
            return;
        }

        if (work.Operation == "fees.snapshot.v1")
        {
            await ExecuteFeeSnapshotAsync(work, cancellationToken);
            return;
        }

        if (work.Operation != "students.snapshot.v1")
        {
            await gateway.FailAsync(work, "UNSUPPORTED_OPERATION", cancellationToken);
            return;
        }

        var pageSize = Math.Clamp(work.Parameters.PageSize, 1, 500);
        var page = new List<StudentRecordV1>(pageSize);
        var pageHashes = new List<string>();
        var recordCount = 0;
        var pageNumber = 1;

        await foreach (var student in adapter.ReadSnapshotAsync(cancellationToken))
        {
            page.Add(student);
            recordCount++;

            if (page.Count == pageSize)
            {
                await UploadOrConfirmAsync(work, pageNumber++, page, pageHashes, cancellationToken);
                page = new List<StudentRecordV1>(pageSize);
            }
        }

        if (page.Count > 0)
        {
            await UploadOrConfirmAsync(work, pageNumber, page, pageHashes, cancellationToken);
        }

        await gateway.CompleteAsync(work, new CompletionManifest(
            pageHashes,
            recordCount,
            new Dictionary<string, string>
            {
                ["adapter_id"] = adapter.Id,
                ["adapter_version"] = adapter.Version,
                ["snapshot_id"] = work.JobId,
            }), cancellationToken);

        logger.LogInformation("Completed student snapshot job {JobId} with {RecordCount} records across {PageCount} pages.", work.JobId, recordCount, pageHashes.Count);
    }

    private async Task ExecuteFeeSnapshotAsync(SyncWork work, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(work.Parameters.PageSize, 1, 500);
        var page = new List<FeeRecordV1>(pageSize);
        var pageHashes = new List<string>();
        var recordCount = 0;
        var pageNumber = 1;

        await foreach (var fee in schoolApi.ReadFeeBalancesAsync(pageSize, cancellationToken))
        {
            page.Add(fee);
            recordCount++;

            if (page.Count == pageSize)
            {
                await UploadFeePageOrConfirmAsync(work, pageNumber++, page, pageHashes, cancellationToken);
                page = new List<FeeRecordV1>(pageSize);
            }
        }

        if (page.Count > 0)
        {
            await UploadFeePageOrConfirmAsync(work, pageNumber, page, pageHashes, cancellationToken);
        }

        await gateway.CompleteAsync(work, new CompletionManifest(
            pageHashes,
            recordCount,
            new Dictionary<string, string>
            {
                ["dataset"] = "fees",
                ["currency"] = "KES",
                ["snapshot_id"] = work.JobId,
            }), cancellationToken);

        logger.LogInformation("Completed fee snapshot job {JobId} with {RecordCount} records across {PageCount} pages.", work.JobId, recordCount, pageHashes.Count);
    }

    private async Task UploadOrConfirmAsync(
        SyncWork work,
        int pageNumber,
        IReadOnlyList<StudentRecordV1> records,
        List<string> pageHashes,
        CancellationToken cancellationToken)
    {
        var hash = GatewayClient.HashPage(records);
        pageHashes.Add(hash);

        if (work.ConfirmedPages.TryGetValue(pageNumber, out var confirmedHash))
        {
            if (!string.Equals(hash, confirmedHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Confirmed page {pageNumber} differs from the current adapter snapshot.");
            }

            return;
        }

        await gateway.UploadPageAsync(work, pageNumber, records, hash, cancellationToken);
    }

    private async Task UploadFeePageOrConfirmAsync(
        SyncWork work,
        int pageNumber,
        IReadOnlyList<FeeRecordV1> records,
        List<string> pageHashes,
        CancellationToken cancellationToken)
    {
        var hash = GatewayClient.HashRecords(records);
        pageHashes.Add(hash);

        if (work.ConfirmedPages.TryGetValue(pageNumber, out var confirmedHash))
        {
            if (!string.Equals(hash, confirmedHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Confirmed fee page {pageNumber} differs from the current adapter snapshot.");
            }

            return;
        }

        await gateway.UploadPageAsync(work, pageNumber, records, hash, cancellationToken);
    }
}
