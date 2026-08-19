using System.Net.Http;
using System.Net.Http.Json;

namespace FeeSyncer.Shared;

public sealed record UpdateCheckResult(
    string CurrentVersion,
    string? LatestVersion,
    string? DownloadUrl,
    string? Sha256,
    long? Size,
    DateTimeOffset? PublishedAt,
    string? ErrorMessage,
    bool NotificationRaised)
{
    public bool Succeeded => ErrorMessage is null;
    public bool IsUpdateAvailable => LatestVersion is not null && LatestVersion != CurrentVersion;
}

public sealed class UpdateChecker : IDisposable
{
    private const string ManifestUrl = "https://s3.munywele.co.ke/fee-syncer/latest.json";
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private string? _lastNotifiedVersion;

    public event Action<string, string>? UpdateAvailable;

    public UpdateChecker()
    {
        _timer = new PeriodicTimer(TimeSpan.FromHours(4));
        AppLogger.Info("Updater", "UpdateChecker initialized");
    }

    public async Task StartAsync()
    {
        await CheckInternalAsync(_cts.Token, notifyAvailable: true);
        while (await _timer.WaitForNextTickAsync(_cts.Token))
            await CheckInternalAsync(_cts.Token, notifyAvailable: true);
    }

    public Task<UpdateCheckResult> CheckAsync(
        CancellationToken ct = default,
        bool notifyAvailable = true) =>
        CheckInternalAsync(ct, notifyAvailable);

    private async Task<UpdateCheckResult> CheckInternalAsync(CancellationToken ct, bool notifyAvailable)
    {
        var current = VersionHelper.GetCurrentVersion();
        var lockTaken = false;
        try
        {
            await _checkLock.WaitAsync(ct);
            lockTaken = true;
            AppLogger.Info("Updater", $"Checking for updates (current: {current})");
            var manifest = await GetLatestRelease(ct);
            var latest = manifest.Version?.TrimStart('v');
            if (string.IsNullOrWhiteSpace(latest))
                throw new InvalidOperationException("The latest release did not include a version tag.");
            var installer = manifest.Installers?.SelfContained;
            if (string.IsNullOrWhiteSpace(installer?.Url))
                throw new InvalidOperationException("The latest release did not include a self-contained installer URL.");
            var notificationRaised = false;

            if (latest is not null && latest != current && latest != _lastNotifiedVersion)
            {
                _lastNotifiedVersion = latest;
                AppLogger.Info("Updater", $"Update available: {current} → {latest}");
                if (notifyAvailable)
                {
                    UpdateAvailable?.Invoke(current, latest);
                    notificationRaised = true;
                }
            }
            else
            {
                AppLogger.Info("Updater", $"No update available (latest: {latest})");
            }

            return new UpdateCheckResult(
                current,
                latest,
                installer.Url,
                installer.Sha256,
                installer.Size,
                manifest.PublishedAt,
                null,
                notificationRaised);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Updater", $"Update check failed: {ex.Message}");
            return new UpdateCheckResult(current, null, null, null, null, null, ex.Message, false);
        }
        finally
        {
            if (lockTaken)
                _checkLock.Release();
        }
    }

    private static async Task<UpdateManifest> GetLatestRelease(CancellationToken ct)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new("FeeSyncer", "1.0"));

        var response = await http.GetAsync(ManifestUrl, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UpdateManifest>(ct)
            ?? throw new InvalidOperationException("The update manifest was empty.");
    }

    private sealed class UpdateManifest
    {
        public string? Version { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
        public InstallerSet? Installers { get; set; }
    }

    private sealed class InstallerSet
    {
        public UpdateArtifact? SelfContained { get; set; }
        public UpdateArtifact? Framework { get; set; }
    }

    private sealed class UpdateArtifact
    {
        public string? Url { get; set; }
        public string? Sha256 { get; set; }
        public long? Size { get; set; }
    }

    public void Dispose()
    {
        AppLogger.Info("Updater", "Disposing UpdateChecker");
        _cts.Cancel();
        _cts.Dispose();
        _timer.Dispose();
    }
}
