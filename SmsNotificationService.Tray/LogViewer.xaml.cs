using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SmsNotificationService.Shared;

namespace SmsNotificationService.Tray;

public partial class LogViewer : Window
{
    private readonly DispatcherTimer _refreshTimer;
    private string? _selectedFilter;

    public LogViewer()
    {
        InitializeComponent();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += (_, _) => LoadLogs();

        Loaded += (_, _) =>
        {
            LoadLogs();
            _refreshTimer.Start();
        };
    }

    private void LoadLogs()
    {
        try
        {
            if (LogTextBox is null) return;

            if (!Directory.Exists(ConfigPathResolver.GetLogDir()))
            {
                LogTextBox.Text = "No log directory found.";
                return;
            }

            var logFiles = Directory.GetFiles(ConfigPathResolver.GetLogDir(), "*.log")
                .OrderByDescending(f => f)
                .Take(1)
                .ToList();

            if (logFiles.Count == 0)
            {
                LogTextBox.Text = "No log files found.";
                return;
            }

            using var stream = new FileStream(logFiles[0], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var allLines = new List<string>();
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (line is not null)
                    allLines.Add(line);
            }

            var lines = allLines.Reverse<string>().Take(500).Reverse().ToList();

            if (!string.IsNullOrEmpty(_selectedFilter))
            {
                lines = lines.Where(l => l.Contains($"[{_selectedFilter}]")).ToList();
            }

            LogTextBox.Text = string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            if (LogTextBox is not null)
                LogTextBox.Text = $"Error reading logs: {ex.Message}";
        }
    }

    private void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedFilter = (FilterCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();

        if (_selectedFilter == "All")
        {
            _selectedFilter = null;
        }

        LoadLogs();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadLogs();
    private void ClearButton_Click(object sender, RoutedEventArgs e) => LogTextBox.Text = string.Empty;

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(LogTextBox.Text);
        MessageBox.Show("Logs copied to clipboard.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(ConfigPathResolver.GetLogDir()))
            Process.Start("explorer.exe", ConfigPathResolver.GetLogDir());
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        _refreshTimer.Stop();
        Hide();
        base.OnClosing(e);
    }
}