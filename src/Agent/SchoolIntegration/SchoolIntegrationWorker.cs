using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Runtime.ExceptionServices;
using FeeSyncer.Shared;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed class SchoolIntegrationWorker(
    GatewayClient gateway,
    IStudentAdapter adapter,
    SchoolApiClient schoolApi,
    AgentWakeSignal wakeSignal,
    AgentMqttEventQueue mqttEvents,
    IOptions<AgentOptions> options,
    ILogger<SchoolIntegrationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextHeartbeat = DateTimeOffset.MinValue;
        var workPollSeconds = options.Value.WorkPollSeconds;
        var longPollSeconds = options.Value.LongPollSeconds;
        var transientFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            SyncWork? work = null;

            try
            {
                if (DateTimeOffset.UtcNow >= nextHeartbeat)
                {
                    var heartbeat = await gateway.HeartbeatAsync(new AgentHeartbeat(
                        VersionHelper.GetCurrentVersion(),
                        ["students.snapshot.v1", "fees.snapshot.v1", "payments.record.v1"],
                        [$"{adapter.Id}:{adapter.Version}"]), stoppingToken);
                    workPollSeconds = Math.Clamp(heartbeat.WorkPollSeconds, 1, 300);
                    var maxLongPoll = Math.Max(0, Math.Min(55, options.Value.RequestTimeoutSeconds - 5));
                    longPollSeconds = Math.Clamp(heartbeat.LongPollMaxSeconds, 0, maxLongPoll);
                    nextHeartbeat = DateTimeOffset.UtcNow.AddSeconds(options.Value.HeartbeatSeconds);
                    mqttEvents.Publish("heartbeat", status: "accepted");
                }

                work = await gateway.LeaseAsync(longPollSeconds, stoppingToken);
                transientFailures = 0;
                if (work is null)
                {
                    if (longPollSeconds == 0)
                    {
                        await WaitForNextWorkAsync(workPollSeconds, stoppingToken);
                    }
                    continue;
                }

                mqttEvents.Publish("progress", status: "active", operation: work.Operation, stage: "preparing");
                await ExecuteWorkAsync(work, stoppingToken);
                mqttEvents.Publish("progress", status: "idle", stage: "reported");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (AgentAuthenticationException exception)
            {
                PublishInterruptedWork(work);
                logger.LogCritical(exception, "Agent authentication failed; stopping work polling until the service is re-enrolled.");
                break;
            }
            catch (AgentProtocolException exception)
            {
                PublishInterruptedWork(work);
                logger.LogCritical(exception, "Agent protocol configuration is incompatible with the gateway; stopping work polling.");
                break;
            }
            catch (AgentLeaseLostException exception)
            {
                logger.LogWarning(exception, "Discarding stale lease for job {JobId}; polling for authoritative work.", work?.JobId ?? "none");
                mqttEvents.Publish("progress", status: "lease_lost", operation: work?.Operation, stage: "reconciling");
            }
            catch (AgentRateLimitException exception)
            {
                PublishInterruptedWork(work);
                var retryAfter = exception.RetryAfter > TimeSpan.Zero ? exception.RetryAfter : TimeSpan.FromSeconds(30);
                logger.LogWarning("Agent gateway rate limit reached; polling resumes in {RetryAfterSeconds} seconds.", retryAfter.TotalSeconds);
                await Task.Delay(retryAfter, stoppingToken);
            }
            catch (HttpRequestException exception)
            {
                PublishInterruptedWork(work);
                transientFailures++;
                var delay = TransientFailureDelay(transientFailures);
                logger.LogWarning(
                    exception,
                    "Agent gateway request failed; polling resumes in {RetryDelaySeconds} seconds. CorrelationId={CorrelationId}",
                    delay.TotalSeconds,
                    gateway.LastRequestId ?? "none");
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException exception)
            {
                PublishInterruptedWork(work);
                transientFailures++;
                var delay = TransientFailureDelay(transientFailures);
                logger.LogWarning(
                    exception,
                    "Agent gateway request timed out; polling resumes in {RetryDelaySeconds} seconds. CorrelationId={CorrelationId}",
                    delay.TotalSeconds,
                    gateway.LastRequestId ?? "none");
                await Task.Delay(delay, stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Agent loop failed for {Operation} job {JobId}; no record payload was logged. CorrelationId={CorrelationId}",
                    work?.Operation ?? "unknown",
                    work?.JobId ?? "none",
                    gateway.LastRequestId ?? "none");

                if (work is not null)
                {
                    try
                    {
                        var failureCode = exception is SchoolApiException schoolApiException
                            ? schoolApiException.FailureCode
                            : exception is AgentRequestRejectedException requestException
                                ? requestException.FailureCode
                            : "SYNC_FAILED";
                        await gateway.FailAsync(work, failureCode, stoppingToken);
                        mqttEvents.Publish("progress", status: "idle", stage: "failure_reported");
                    }
                    catch (Exception failureException)
                    {
                        PublishInterruptedWork(work);
                        logger.LogError(failureException, "Could not report failure for job {JobId}.", work.JobId);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(options.Value.IdleDelaySeconds), stoppingToken);
            }
        }
    }

    private async Task WaitForNextWorkAsync(int delaySeconds, CancellationToken cancellationToken)
    {
        // MQTT only accelerates the periodic HTTP loop; it never gates work discovery.
        await wakeSignal.WaitAsync(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
    }

    private async Task ExecuteWorkAsync(SyncWork work, CancellationToken cancellationToken)
    {
        using var workCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewalTask = RenewLeaseLoopAsync(work, workCts.Token);
        var processingTask = ExecuteWorkWithoutRenewalAsync(work, workCts.Token);

        var completedTask = await Task.WhenAny(processingTask, renewalTask);
        if (completedTask == renewalTask)
        {
            Exception renewalFailure;
            try
            {
                await renewalTask;
                renewalFailure = new InvalidOperationException("Lease renewal stopped unexpectedly.");
            }
            catch (Exception exception)
            {
                renewalFailure = exception;
            }

            workCts.Cancel();
            try
            {
                await processingTask;
            }
            catch (OperationCanceledException) when (workCts.IsCancellationRequested)
            {
            }
            catch (Exception processingException)
            {
                logger.LogWarning(
                    processingException,
                    "Work processing for job {JobId} stopped after lease renewal failed.",
                    work.JobId);
            }

            ExceptionDispatchInfo.Capture(renewalFailure).Throw();
        }

        try
        {
            await processingTask;
        }
        finally
        {
            workCts.Cancel();
            try
            {
                await renewalTask;
            }
            catch (OperationCanceledException) when (workCts.IsCancellationRequested)
            {
            }
        }
    }

    private async Task RenewLeaseLoopAsync(SyncWork work, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, options.Value.LeaseRenewalSeconds));
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(interval, cancellationToken);
            await gateway.RenewLeaseAsync(work, cancellationToken);
        }
    }

    private async Task ExecuteWorkWithoutRenewalAsync(SyncWork work, CancellationToken cancellationToken)
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

            mqttEvents.Publish("progress", status: "active", operation: work.Operation, stage: "processing");
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
        var expectedRecordCount = await schoolApi.GetExpectedStudentCountAsync(cancellationToken);
        await gateway.ReportExpectedRecordCountAsync(work, expectedRecordCount, cancellationToken);
        mqttEvents.Publish("progress", status: "active", operation: work.Operation, stage: "processing");

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
                await UploadOrConfirmAsync(work, pageNumber++, page, pageHashes, expectedRecordCount, cancellationToken);
                page = new List<StudentRecordV1>(pageSize);
            }
        }

        if (page.Count > 0)
        {
            await UploadOrConfirmAsync(work, pageNumber, page, pageHashes, expectedRecordCount, cancellationToken);
        }

        var completion = await gateway.CompleteAsync(work, new CompletionManifest(pageHashes, recordCount), cancellationToken);

        logger.LogInformation(
            "Student snapshot upload accepted for job {JobId} with {RecordCount} records across {PageCount} pages. ServerStatus={ServerStatus} MaterializationCompleted={MaterializationCompleted}",
            work.JobId, recordCount, pageHashes.Count, completion.Status, completion.Completed);
    }

    private async Task ExecuteFeeSnapshotAsync(SyncWork work, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(work.Parameters.PageSize, 1, 500);
        var expectedRecordCount = await schoolApi.GetExpectedFeeCountAsync(cancellationToken);
        await gateway.ReportExpectedRecordCountAsync(work, expectedRecordCount, cancellationToken);
        mqttEvents.Publish("progress", status: "active", operation: work.Operation, stage: "processing");

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
                await UploadFeePageOrConfirmAsync(work, pageNumber++, page, pageHashes, expectedRecordCount, cancellationToken);
                logger.LogInformation("Uploaded fee page {PageNumber} for job {JobId} with {RecordCount} records.", pageNumber - 1, work.JobId, page.Count);
                page = new List<FeeRecordV1>(pageSize);
            }
        }

        if (page.Count > 0)
        {
            await UploadFeePageOrConfirmAsync(work, pageNumber, page, pageHashes, expectedRecordCount, cancellationToken);
            logger.LogInformation("Uploaded fee page {PageNumber} for job {JobId} with {RecordCount} records.", pageNumber, work.JobId, page.Count);
        }

        var completion = await gateway.CompleteAsync(work, new CompletionManifest(pageHashes, recordCount), cancellationToken);

        logger.LogInformation(
            "Fee snapshot upload accepted for job {JobId} with {RecordCount} records across {PageCount} pages. ServerStatus={ServerStatus} MaterializationCompleted={MaterializationCompleted}",
            work.JobId, recordCount, pageHashes.Count, completion.Status, completion.Completed);
    }

    private async Task UploadOrConfirmAsync(
        SyncWork work,
        int pageNumber,
        IReadOnlyList<StudentRecordV1> records,
        List<string> pageHashes,
        int expectedRecordCount,
        CancellationToken cancellationToken)
    {
        var page = GatewayClient.SerializePage(records);
        pageHashes.Add(page.ContentHash);

        if (work.ConfirmedPages.TryGetValue(pageNumber, out var confirmedHash))
        {
            if (!string.Equals(page.ContentHash, confirmedHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Confirmed page {pageNumber} differs from the current adapter snapshot.");
            }

            return;
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Uploading student snapshot page. JobId={JobId} PageNumber={PageNumber} RecordCount={RecordCount} ContentHash={ContentHash} RedactedSamples={RedactedSamples}",
                work.JobId,
                pageNumber,
                records.Count,
                page.ContentHash,
                CreateRedactedStudentSamples(records));
        }

        await gateway.UploadPageAsync(work, pageNumber, page, expectedRecordCount, cancellationToken);
    }

    internal static string CreateRedactedStudentSamples(IReadOnlyList<StudentRecordV1> records) =>
        JsonSerializer.Serialize(records.Take(3).Select((record, index) => new
        {
            sample_index = index + 1,
            admission_number = Redact(record.AdmissionNumber),
            record.ClassIdentifier,
            record.EnrollmentStatus,
            source_student_id = Redact(record.SourceStudentId),
            record.SourceUpdatedAt,
            name = Redact(record.Name),
            phone = Redact(record.Phone),
            record.Stream,
            record.Form,
            record.Term,
            record.Year,
            parent_name = Redact(record.ParentName),
            balance = Redact(record.Balance),
            record.ClassNumber,
        }), new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string? Redact(string? value) => value is null ? null : "[redacted]";

    private static TimeSpan TransientFailureDelay(int failureCount)
    {
        var seconds = Math.Min(60, Math.Pow(2, Math.Min(failureCount - 1, 6)));
        return TimeSpan.FromSeconds(seconds * (0.8 + Random.Shared.NextDouble() * 0.4));
    }

    private void PublishInterruptedWork(SyncWork? work)
    {
        if (work is not null)
        {
            mqttEvents.Publish("progress", status: "lease_lost", operation: work.Operation, stage: "reconciling");
        }
    }

    private async Task UploadFeePageOrConfirmAsync(
        SyncWork work,
        int pageNumber,
        IReadOnlyList<FeeRecordV1> records,
        List<string> pageHashes,
        int expectedRecordCount,
        CancellationToken cancellationToken)
    {
        var page = GatewayClient.SerializePage(records);
        pageHashes.Add(page.ContentHash);

        if (work.ConfirmedPages.TryGetValue(pageNumber, out var confirmedHash))
        {
            if (!string.Equals(page.ContentHash, confirmedHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Confirmed fee page {pageNumber} differs from the current adapter snapshot.");
            }

            return;
        }

        await gateway.UploadPageAsync(work, pageNumber, page, expectedRecordCount, cancellationToken);
    }
}
