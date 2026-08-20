using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using FeeSyncer.Shared;

namespace FeeSyncer.Tray;

public partial class UpdateCheckWindow : Window
{
    private readonly UpdateChecker _updater;
    private readonly UpdateDownloadService _downloader;
    private CancellationTokenSource _cancellation = new();
    private UpdateCheckResult? _update;
    private bool _busy;
    private bool _closeRequested;
    private bool _completed;

    public UpdateCheckWindow(UpdateChecker updater, UpdateDownloadService? downloader = null)
    {
        InitializeComponent();
        _updater = updater;
        _downloader = downloader ?? new UpdateDownloadService();
        CurrentVersionText.Text = VersionHelper.GetCurrentVersion();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var flavor = InstallerFlavorDetector.Detect();
        var result = await _updater.CheckAsync(
            _cancellation.Token,
            notifyAvailable: false,
            installerFlavor: flavor);

        Progress.IsIndeterminate = false;
        Progress.Value = 100;
        LatestVersionText.Text = result.LatestVersion ?? "Unavailable";
        PublishedText.Text = result.PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz") ?? "Unavailable";
        var flavorName = result.InstallerFlavor == UpdateInstallerFlavor.Framework
            ? "Framework-dependent"
            : "Self-contained";
        InstallerText.Text = result.Size is > 0
            ? $"{flavorName} Windows x64 ({FormatSize(result.Size.Value)})"
            : $"{flavorName} Windows x64";
        _update = result;

        if (_cancellation.IsCancellationRequested)
        {
            StatusTitleText.Text = "Update check cancelled";
            StatusDetailsText.Text = "No changes were made. You can run the check again at any time.";
            StatusTitleText.Foreground = Brushes.DarkGoldenrod;
        }
        else if (!result.Succeeded)
        {
            StatusTitleText.Text = "Unable to check for updates";
            StatusDetailsText.Text = result.ErrorMessage ?? "Public release information could not be retrieved.";
            StatusTitleText.Foreground = Brushes.Firebrick;
        }
        else if (result.IsUpdateAvailable)
        {
            StatusTitleText.Text = "A new version is available";
            var integrity = string.IsNullOrWhiteSpace(result.Sha256)
                ? string.Empty
                : $" SHA-256: {result.Sha256}";
            StatusDetailsText.Text = $"Version {result.LatestVersion} is available. You are currently running {result.CurrentVersion}.{integrity}";
            StatusTitleText.Foreground = Brushes.ForestGreen;
            DownloadButton.Visibility = Visibility.Visible;
            DownloadOnlyButton.Visibility = Visibility.Visible;
            UnsignedWarning.Visibility = Visibility.Visible;
        }
        else
        {
            StatusTitleText.Text = "FeeSyncer is up to date";
            StatusDetailsText.Text = $"Version {result.CurrentVersion} is the latest published release.";
            StatusTitleText.Foreground = Brushes.ForestGreen;
        }

        _completed = true;
        CloseButton.Content = "Close";
        CloseButton.IsEnabled = true;
        if (_closeRequested)
            Close();
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_update is null || !_update.IsUpdateAvailable)
            return;

        if (_cancellation.IsCancellationRequested)
        {
            _cancellation.Dispose();
            _cancellation = new CancellationTokenSource();
        }

        var confirmation = MessageBox.Show(
            $"FeeSyncer {_update.LatestVersion} will be downloaded and verified before Windows asks for administrator permission.\n\n" +
            "The installer is currently unsigned, so the UAC dialog will show Unknown publisher. Continue?",
            "Install FeeSyncer Update",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
            return;

        _busy = true;
        _completed = false;
        DownloadButton.IsEnabled = false;
        DownloadOnlyButton.IsEnabled = false;
        CloseButton.Content = "Cancel";
        Progress.IsIndeterminate = false;
        Progress.Value = 0;
        StatusTitleText.Text = "Downloading update...";
        StatusTitleText.Foreground = Brushes.DodgerBlue;
        StatusDetailsText.Text = "Downloading the installer from the Munywele public update channel.";

        var progress = new Progress<UpdateDownloadProgress>(value =>
        {
            Progress.Value = value.Percentage;
            ProgressDetailsText.Text = $"{FormatSize(value.BytesReceived)} of {FormatSize(value.TotalBytes)} ({value.Percentage}%)";
        });

        try
        {
            using var download = await _downloader.DownloadAsync(_update, progress, _cancellation.Token);
            var installerPath = download.Path;
            StatusTitleText.Text = "Update verified";
            StatusTitleText.Foreground = Brushes.ForestGreen;
            StatusDetailsText.Text = "The installer size and SHA-256 checksum are valid. Waiting for administrator permission...";
            ProgressDetailsText.Text = $"Verified: {Path.GetFileName(installerPath)}";

            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                WorkingDirectory = Path.GetDirectoryName(installerPath),
                UseShellExecute = true,
                Verb = "runas",
            };
            startInfo.ArgumentList.Add("/VERYSILENT");
            startInfo.ArgumentList.Add("/SUPPRESSMSGBOXES");
            startInfo.ArgumentList.Add("/NORESTART");
            startInfo.ArgumentList.Add("/SELFUPDATE=1");
            startInfo.ArgumentList.Add("/RESTARTTRAY=1");

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows did not start the update installer.");
            AppLogger.Info("Updater", $"Started verified installer {_update.LatestVersion} (PID {process.Id}); shutting down tray.");
            _busy = false;
            _completed = true;
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            ShowInstallFailure("Update cancelled", "The installer download was cancelled. No application files were changed.", Brushes.DarkGoldenrod);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            ShowInstallFailure("Administrator permission cancelled", "Windows did not start the installer. FeeSyncer remains unchanged.", Brushes.DarkGoldenrod);
        }
        catch (Exception exception)
        {
            AppLogger.Error("Updater", "Self-update failed", exception);
            ShowInstallFailure("Unable to install update", exception.Message, Brushes.Firebrick);
        }
    }

    private void DownloadOnly_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_update?.DownloadUrl))
            Process.Start(new ProcessStartInfo(_update.DownloadUrl) { UseShellExecute = true });
    }

    private void ShowInstallFailure(string title, string details, Brush color)
    {
        _busy = false;
        _completed = true;
        Progress.IsIndeterminate = false;
        StatusTitleText.Text = title;
        StatusTitleText.Foreground = color;
        StatusDetailsText.Text = details;
        DownloadButton.IsEnabled = true;
        DownloadOnlyButton.IsEnabled = true;
        CloseButton.Content = "Close";
        CloseButton.IsEnabled = true;
        if (_closeRequested)
            Close();
    }

    private static string FormatSize(long bytes)
    {
        const double megabyte = 1024 * 1024;
        return $"{bytes / megabyte:N1} MB";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_completed && !_busy)
        {
            Close();
            return;
        }

        StatusTitleText.Text = "Cancelling update check...";
        StatusDetailsText.Text = _busy
            ? "Stopping the installer download. No application files have been changed."
            : "Waiting for the current network request to stop.";
        CloseButton.IsEnabled = false;
        _closeRequested = true;
        _cancellation.Cancel();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_completed && !_busy)
            return;

        e.Cancel = true;
        StatusTitleText.Text = _busy ? "Cancelling update download..." : "Cancelling update check...";
        StatusDetailsText.Text = _busy
            ? "Stopping the installer download. No application files have been changed."
            : "Waiting for the current network request to stop.";
        CloseButton.IsEnabled = false;
        _closeRequested = true;
        _cancellation.Cancel();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cancellation.Dispose();
        base.OnClosed(e);
    }
}
