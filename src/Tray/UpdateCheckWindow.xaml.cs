using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using FeeSyncer.Shared;

namespace FeeSyncer.Tray;

public partial class UpdateCheckWindow : Window
{
    private readonly UpdateChecker _updater;
    private readonly CancellationTokenSource _cancellation = new();
    private string? _downloadUrl;
    private bool _completed;

    public UpdateCheckWindow(UpdateChecker updater)
    {
        InitializeComponent();
        _updater = updater;
        CurrentVersionText.Text = VersionHelper.GetCurrentVersion();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var result = await _updater.CheckAsync(_cancellation.Token, notifyAvailable: false);

        Progress.IsIndeterminate = false;
        Progress.Value = 100;
        LatestVersionText.Text = result.LatestVersion ?? "Unavailable";
        PublishedText.Text = result.PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz") ?? "Unavailable";
        InstallerText.Text = result.Size is > 0
            ? $"Self-contained Windows x64 ({FormatSize(result.Size.Value)})"
            : "Self-contained Windows x64";
        _downloadUrl = result.DownloadUrl;

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
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_downloadUrl))
            Process.Start(new ProcessStartInfo(_downloadUrl) { UseShellExecute = true });
    }

    private static string FormatSize(long bytes)
    {
        const double megabyte = 1024 * 1024;
        return $"{bytes / megabyte:N1} MB";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_completed)
        {
            Close();
            return;
        }

        StatusTitleText.Text = "Cancelling update check...";
        StatusDetailsText.Text = "Waiting for the current network request to stop.";
        CloseButton.IsEnabled = false;
        _cancellation.Cancel();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_completed)
            return;

        e.Cancel = true;
        StatusTitleText.Text = "Cancelling update check...";
        StatusDetailsText.Text = "Waiting for the current network request to stop.";
        CloseButton.IsEnabled = false;
        _cancellation.Cancel();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cancellation.Dispose();
        base.OnClosed(e);
    }
}
