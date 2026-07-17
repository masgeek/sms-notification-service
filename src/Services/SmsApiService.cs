// Copyright (c) Munywele Consulting LTD. All rights reserved.

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmsNotificationService.Configuration;
using SmsNotificationService.Models;

namespace SmsNotificationService.Services;

/// <summary>
/// Sends SMS notifications via the configured HTTP API.
/// This service performs a single send attempt per call. Retry/backoff scheduling
/// is the caller's responsibility (e.g. a queue worker), using <see cref="CalculateRetryAfter"/>
/// to compute when to re-enqueue a failed notification.
/// </summary>
public sealed class SmsApiService : ISmsSender
{
    private readonly ILogger<SmsApiService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _smsApiUrl;
    private readonly string _authorizationToken;
    private readonly int _retryBackoffSeconds;

    /// <summary>
    /// 5xx and 408 (request timeout) are treated as transient/retryable.
    /// Other 4xx responses indicate a bad request (invalid phone number, malformed
    /// payload, auth failure) that will not succeed on retry.
    /// </summary>
    private static bool IsTransient(int statusCode) => statusCode is 408 or >= 500;

    public SmsApiService(ILogger<SmsApiService> logger, IHttpClientFactory httpClientFactory,
        IOptions<SmsServiceOptions> options)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _smsApiUrl = options.Value.SmsApiUrl;
        _authorizationToken = options.Value.AuthorizationToken;
        _retryBackoffSeconds = options.Value.RetryBackoffSeconds;
    }

    public async Task<SendResult> SendAsync(SmsNotification notification, CancellationToken cancellationToken = default)
    {
        try
        {
            using var httpClient = _httpClientFactory.CreateClient("SmsApi");
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _authorizationToken);

            var payload = new
            {
                id = notification.Id,
                phone_number = notification.PhoneNumber,
                mpesa_code = notification.MpesaCode,
                admission_no = notification.AdmNo,
                student_name = notification.StudNames,
                amount = notification.Amount,
                receipt_no = notification.ReceiptNo,
                dated = notification.Dated
            };

            _logger.LogDebug("[SMS] Sending notification {Id} to {Phone}", notification.Id,
                Mask(notification.PhoneNumber));

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync(_smsApiUrl, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[SMS] Sent notification {Id} to {Phone}", notification.Id,
                    Mask(notification.PhoneNumber));
                return SendResult.Ok();
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var statusCode = (int)response.StatusCode;
            var transient = IsTransient(statusCode);

            _logger.LogWarning(
                "[SMS] Notification {Id} to {Phone} failed — HTTP {StatusCode}: {Body} (transient: {Transient})",
                notification.Id, Mask(notification.PhoneNumber), statusCode, body, transient);

            return SendResult.Fail(body, retryable: transient);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancelled — not a send failure, let it propagate.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SMS] Notification {Id} to {Phone} exception", notification.Id,
                Mask(notification.PhoneNumber));
            // Network-level failures (timeouts, DNS, connection reset) are treated as transient.
            return SendResult.Fail(ex.Message, retryable: true);
        }
    }

    /// <summary>
    /// Computes when a failed, retryable notification should next be attempted.
    /// Uses exponential backoff off the configured base interval, with jitter to
    /// avoid synchronized retry spikes across many notifications.
    /// </summary>
    public DateTimeOffset CalculateRetryAfter(int retryCount)
    {
        var exponent = Math.Max(0, retryCount - 1);
        var baseDelay = _retryBackoffSeconds * Math.Pow(2, exponent);

        // +/- up to 20% jitter.
        var jitterFactor = 1.0 + (Random.Shared.NextDouble() * 0.4 - 0.2);
        var delaySeconds = baseDelay * jitterFactor;

        return DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
    }

    private static string Mask(string? phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber) || phoneNumber.Length <= 4)
        {
            return "***";
        }

        return string.Concat(phoneNumber.AsSpan(0, 3), "***", phoneNumber.AsSpan(phoneNumber.Length - 2));
    }
}