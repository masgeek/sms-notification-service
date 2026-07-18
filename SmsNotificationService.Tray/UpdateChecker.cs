using System.Net.Http;
using System.Net.Http.Json;
using SmsNotificationService.Shared;

namespace SmsNotificationService.Tray;

internal sealed class UpdateChecker : IDisposable
{
    private const string RepoUrl = "https://api.github.com/repos/masgeek/sms-notification-service";
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private string? _lastNotifiedVersion;

    public event Action<string, string>? UpdateAvailable;

    public UpdateChecker()
    {
        _timer = new PeriodicTimer(TimeSpan.FromHours(4));
    }

    public async Task StartAsync()
    {
        await CheckInternalAsync(_cts.Token);
        while (await _timer.WaitForNextTickAsync(_cts.Token))
            await CheckInternalAsync(_cts.Token);
    }

    public Task CheckAsync(CancellationToken ct = default) => CheckInternalAsync(ct);

    private async Task CheckInternalAsync(CancellationToken ct)
    {
        try
        {
            var current = VersionHelper.GetCurrentVersion();
            var latest = await GetLatestVersion(ct);

            if (latest is not null && latest != current && latest != _lastNotifiedVersion)
            {
                _lastNotifiedVersion = latest;
                UpdateAvailable?.Invoke(current, latest);
            }
        }
        catch { /* Network error — silent */ }
    }

    private static async Task<string?> GetLatestVersion(CancellationToken ct)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new("SmsNotificationService", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new("application/vnd.github+json"));

        var response = await http.GetAsync($"{RepoUrl}/releases/latest", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<GitHubRelease>(ct);
        return json?.TagName?.TrimStart('v');
    }

    private sealed class GitHubRelease
    {
        public string? TagName { get; set; }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _timer.Dispose();
    }
}
