using SmsNotificationService.Configuration;
using SmsNotificationService.Models;
using Microsoft.Extensions.Options;

namespace SmsNotificationService.Services;

public class SmsApiService : ISmsSender
{
    private readonly ILogger<SmsApiService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _smsApiUrl;
    private readonly string _authorizationToken;
    private readonly int _retryBackoffSeconds;

    private const int MaxRetries = 3;

    public SmsApiService(ILogger<SmsApiService> logger, IHttpClientFactory httpClientFactory, IOptions<SmsServiceOptions> options)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _smsApiUrl = options.Value.SmsApiUrl;
        _authorizationToken = options.Value.AuthorizationToken;
        _retryBackoffSeconds = options.Value.RetryBackoffSeconds;
    }

    public async Task<bool> SendAsync(SmsNotification notification)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Authorization = new("Bearer", _authorizationToken);

                var payload = new
                {
                    id = notification.id,
                    phone_number = notification.phone_number,
                    mpesa_code = notification.mpesa_code,
                    admission_no = notification.adm_no,
                    student_name = notification.stud_names,
                    amount = notification.amount,
                    receipt_no = notification.receipt_no,
                    dated = notification.dated
                };

                _logger.LogDebug("[SMS] Sending notification {Id} to {Phone} (attempt {Attempt}/{Max})",
                    notification.id, notification.phone_number, attempt, MaxRetries);

                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(_smsApiUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[SMS] Sent notification {Id} to {Phone}", notification.id, notification.phone_number);
                    return true;
                }

                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("[SMS] Notification {Id} to {Phone} failed — HTTP {StatusCode}: {Body} (attempt {Attempt}/{Max})",
                    notification.id, notification.phone_number, (int)response.StatusCode, body, attempt, MaxRetries);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SMS] Notification {Id} to {Phone} exception (attempt {Attempt}/{Max})",
                    notification.id, notification.phone_number, attempt, MaxRetries);
            }

            if (attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogInformation("[SMS] Retrying notification {Id} in {Delay}s...", notification.id, delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }

        return false;
    }

    public DateTimeOffset CalculateRetryAfter(int retryCount)
    {
        var backoff = TimeSpan.FromSeconds(_retryBackoffSeconds * Math.Pow(2, retryCount - 1));
        return DateTime.UtcNow.Add(backoff);
    }
}
