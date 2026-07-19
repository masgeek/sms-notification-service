using System.Drawing;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using SmsNotificationService.Shared;
using SmsNotificationService.Shared.Models;

namespace SmsNotificationService.Tray;

internal sealed class TrayIcon : IDisposable
{
    private readonly ServiceMonitor _monitor;
    private readonly UpdateChecker _updater;
    private readonly TaskbarIcon _icon;
    private readonly DispatcherTimer _statusTimer;

    private StatusWindow? _statusWindow;
    private LogViewer? _logViewer;
    private SendNotificationDialog? _sendDialog;
    private ConfigEditor? _configEditor;

    public TrayIcon()
    {
        AppLogger.Info("Tray", "Initializing TrayIcon...");

        _monitor = new ServiceMonitor();
        _updater = new UpdateChecker();

        _icon = new TaskbarIcon
        {
            Icon = CreateIcon(ServiceControllerStatus.Stopped),
            ToolTipText = "SmsNotificationService — Starting..."
        };

        _icon.LeftClickCommand = new DelegateCommand(_ => ShowStatusWindow());
        _icon.ContextMenu = BuildContextMenu();
        _icon.ForceCreate();

        _monitor.StatusChanged += OnStatusChanged;
        _updater.UpdateAvailable += OnUpdateAvailable;

        _ = _monitor.StartAsync();
        _ = _updater.StartAsync();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _statusTimer.Tick += (_, _) => UpdateIcon();
        _statusTimer.Start();
    }

    private void OnStatusChanged(ServiceStatusInfo info)
    {
        AppLogger.Info("Tray", $"Status changed: {StatusHelper.FormatStatus(info.Status)} (via {info.DetectionMethod})");

        Application.Current.Dispatcher.Invoke(() =>
        {
            _icon.Icon = CreateIcon(info.Status);
            _icon.ToolTipText = $"SmsNotificationService — {StatusHelper.FormatStatus(info.Status)} (v{info.Version})";

            if (info.Status == ServiceControllerStatus.Stopped)
            {
                _icon.ShowNotification(
                    "Service Stopped",
                    "SmsNotificationService has stopped unexpectedly.",
                    NotificationIcon.Warning);
            }
        });
    }

    private void OnUpdateAvailable(string current, string latest)
    {
        AppLogger.Info("Tray", $"Update available: current={current}, latest={latest}");

        Application.Current.Dispatcher.Invoke(() =>
        {
            _icon.ShowNotification(
                "Update Available",
                $"New version {latest} is available.\nCurrent: {current}",
                NotificationIcon.Info);
        });
    }

    private void UpdateIcon() => _icon.Icon = CreateIcon(_monitor.Current.Status);

    private void ShowStatusWindow()
    {
        _statusWindow ??= new StatusWindow(_monitor);
        _statusWindow.Show();
        _statusWindow.Activate();
    }

    private void ShowLogViewer()
    {
        _logViewer ??= new LogViewer();
        _logViewer.Show();
        _logViewer.Activate();
    }

    private void ShowSendDialog()
    {
        _sendDialog ??= new SendNotificationDialog();
        _sendDialog.Show();
        _sendDialog.Activate();
    }

    private void ShowConfigEditor()
    {
        _configEditor ??= new ConfigEditor(_monitor);
        _configEditor.Show();
        _configEditor.Activate();
    }

    private async void ShowConnectionValidator()
    {
        var validator = new ConnectionValidator();
        var result = await validator.ValidateAsync();
        var level = result.AllPassed ? NotificationIcon.Info : NotificationIcon.Warning;
        _icon.ShowNotification("Connection Validation", result.Summary, level);
    }

    private void CheckForUpdates() => _ = _updater.CheckAsync();

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        menu.Items.Add(new MenuItem { Header = "Status", Command = new DelegateCommand(_ => ShowStatusWindow()) });
        menu.Items.Add(new MenuItem { Header = "View Logs", Command = new DelegateCommand(_ => ShowLogViewer()) });
        menu.Items.Add(new MenuItem { Header = "Send Notification", Command = new DelegateCommand(_ => ShowSendDialog()) });
        menu.Items.Add(new MenuItem { Header = "Validate Connections", Command = new DelegateCommand(_ => ShowConnectionValidator()) });
        menu.Items.Add(new MenuItem { Header = "Settings", Command = new DelegateCommand(_ => ShowConfigEditor()) });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Start Service", Command = new DelegateCommand(_ => _monitor.StartService()) });
        menu.Items.Add(new MenuItem { Header = "Stop Service", Command = new DelegateCommand(_ => _monitor.StopService()) });
        menu.Items.Add(new MenuItem { Header = "Restart Service", Command = new DelegateCommand(_ => _monitor.RestartService()) });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Check for Updates", Command = new DelegateCommand(_ => CheckForUpdates()) });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Exit", Command = new DelegateCommand(_ => Exit()) });

        return menu;
    }

    private void Exit() => Application.Current.Shutdown();

    private static Icon CreateIcon(ServiceControllerStatus status)
    {
        var color = status switch
        {
            ServiceControllerStatus.Running => Color.FromArgb(34, 197, 94),
            ServiceControllerStatus.Stopped => Color.FromArgb(239, 68, 68),
            _ => Color.FromArgb(234, 179, 8)
        };

        var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var brush = new SolidBrush(color);
        graphics.FillEllipse(brush, 1, 1, 14, 14);

        using var pen = new Pen(Color.FromArgb(80, 0, 0, 0), 1);
        graphics.DrawEllipse(pen, 1, 1, 14, 14);

        return Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        AppLogger.Info("Tray", "Disposing TrayIcon");
        _monitor.Dispose();
        _updater.Dispose();
        _icon.Dispose();
        _statusTimer.Stop();
    }
}

internal sealed class DelegateCommand : System.Windows.Input.ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public DelegateCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
