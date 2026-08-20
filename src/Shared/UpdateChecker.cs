using System.Net.Http;
using System.Net.Http.Json;

namespace FeeSyncer.Shared;

public enum UpdateInstallerFlavor
{
    SelfContained,
    Framework,
}

public sealed record UpdateCheckResult(
    string CurrentVersion,
    string? LatestVersion,
    UpdateInstallerFlavor InstallerFlavor,
    string? DownloadUrl,
    string? Sha256,
    long? Size,
    DateTimeOffset? PublishedAt,
    string? ErrorMessage,
    bool NotificationRaised)
{
    public bool Succeeded => ErrorMessage is null;
    public bool IsUpdateAvailable =>
        Version.TryParse(CurrentVersion, out var current) &&
        Version.TryParse(LatestVersion, out var latest) &&
        latest > current;
}

public sealed class UpdateChecker : IDisposable
{
    private const string PrimaryManifestUrl = "https://s3.munywele.co.ke/fee-syncer/latest.json";
    private const string FallbackManifestUrl =
        "https://github.com/masgeek/sms-notification-service/releases/latest/download/latest.json";
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private string? _lastNotifiedVersion;

    public event Action<string, string>? UpdateAvailable;

    public UpdateChecker()
        : this(new HttpClient(), ownsHttpClient: true)
    {
    }

    public UpdateChecker(HttpClient httpClient)
        : this(httpClient, ownsHttpClient: false)
    {
    }

    private UpdateChecker(HttpClient httpClient, bool ownsHttpClient)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
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
        bool notifyAvailable = true,
        UpdateInstallerFlavor installerFlavor = UpdateInstallerFlavor.SelfContained) =>
        CheckInternalAsync(ct, notifyAvailable, installerFlavor);

    private async Task<UpdateCheckResult> CheckInternalAsync(
        CancellationToken ct,
        bool notifyAvailable,
        UpdateInstallerFlavor installerFlavor = UpdateInstallerFlavor.SelfContained)
    {
        var current = VersionHelper.GetCurrentVersion();
        var lockTaken = false;
        try
        {
            await _checkLock.WaitAsync(ct);
            lockTaken = true;
            AppLogger.Info("Updater", $"Checking for updates (current: {current})");
            var (manifest, latest, installer) = await GetLatestRelease(installerFlavor, ct);
            var notificationRaised = false;
            var isUpdateAvailable = IsNewerVersion(latest, current);

            if (isUpdateAvailable && latest != _lastNotifiedVersion)
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
                installerFlavor,
                installer!.Url,
                installer.Sha256,
                installer.Size,
                manifest.PublishedAt,
                null,
                notificationRaised);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Updater", $"Update check failed: {ex.Message}");
            return new UpdateCheckResult(current, null, installerFlavor, null, null, null, null, ex.Message, false);
        }
        finally
        {
            if (lockTaken)
                _checkLock.Release();
        }
    }

    private static bool IsNewerVersion(string latest, string current) =>
        Version.TryParse(latest, out var latestVersion) &&
        Version.TryParse(current, out var currentVersion) &&
        latestVersion > currentVersion;

    private static void ValidateInstaller(UpdateArtifact? installer, string version)
    {
        if (!Uri.TryCreate(installer?.Url, UriKind.Absolute, out var uri) ||
            !TrustedUpdateSource.IsTrustedInstaller(uri, version))
        {
            throw new InvalidOperationException("The latest release included an invalid installer URL.");
        }

        if (installer.Size is null or <= 0)
            throw new InvalidOperationException("The latest release did not include a valid installer size.");

        try
        {
            if (string.IsNullOrWhiteSpace(installer.Sha256) || Convert.FromHexString(installer.Sha256).Length != 32)
                throw new FormatException();
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("The latest release did not include a valid SHA-256 checksum.");
        }
    }

    private async Task<(UpdateManifest Manifest, string Version, UpdateArtifact Installer)> GetLatestRelease(
        UpdateInstallerFlavor installerFlavor,
        CancellationToken ct)
    {
        try
        {
            return await GetValidatedRelease(PrimaryManifestUrl, installerFlavor, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception primaryException)
        {
            AppLogger.Warn("Updater", $"Primary update source failed; trying GitHub Releases: {primaryException.Message}");
            try
            {
                return await GetValidatedRelease(FallbackManifestUrl, installerFlavor, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception fallbackException)
            {
                throw new InvalidOperationException(
                    $"Primary and fallback update sources failed. S3: {primaryException.Message} GitHub: {fallbackException.Message}",
                    fallbackException);
            }
        }
    }

    private async Task<(UpdateManifest Manifest, string Version, UpdateArtifact Installer)> GetValidatedRelease(
        string manifestUrl,
        UpdateInstallerFlavor installerFlavor,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
        request.Headers.UserAgent.ParseAdd("FeeSyncer/1.0");
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var manifest = await response.Content.ReadFromJsonAsync<UpdateManifest>(ct)
            ?? throw new InvalidOperationException("The update manifest was empty.");
        var latest = manifest.Version?.TrimStart('v');
        if (!Version.TryParse(latest, out _))
            throw new InvalidOperationException("The latest release did not include a version tag.");

        var installer = installerFlavor == UpdateInstallerFlavor.Framework
            ? manifest.Installers?.Framework
            : manifest.Installers?.SelfContained;
        ValidateInstaller(installer, latest);

        return (manifest, latest, installer!);
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
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
