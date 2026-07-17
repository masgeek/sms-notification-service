// Copyright (c) Munywele Consulting LTD. All rights reserved.

using SmsNotificationService.Models;

namespace SmsNotificationService.Services;

public interface ISmsSender
{
    Task<SendResult> SendAsync(SmsNotification notification, CancellationToken cancellationToken = default);

    DateTimeOffset CalculateRetryAfter(int retryCount);
}

public sealed class SendResult
{
    public bool Success { get; private init; }
    public string? ErrorMessage { get; private init; }
    public bool Retryable { get; private init; }

    public static SendResult Ok() => new() { Success = true };

    public static SendResult Fail(string? errorMessage, bool retryable = false) =>
        new() { Success = false, ErrorMessage = errorMessage, Retryable = retryable };
}