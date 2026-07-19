using System.Windows;
using SmsNotificationService.Shared;

namespace SmsNotificationService.Tray;

public partial class App : Application
{
    private TrayIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            AppLogger.Initialize("TrayApp");
            AppLogger.Info("App", $"SmsNotificationService Tray starting (v{VersionHelper.GetCurrentVersion()})");
            AppLogger.Info("App", $"OS: {Environment.OSVersion}, .NET: {Environment.Version}");

            base.OnStartup(e);

            _trayIcon = new TrayIcon();

            AppLogger.Info("App", "Tray app initialized successfully");
        }
        catch (Exception ex)
        {
            AppLogger.Error("App", "Fatal error during startup", ex);
            MessageBox.Show($"Startup failed: {ex.Message}", "SmsNotificationService Tray", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            AppLogger.Info("App", "Tray app shutting down");
            _trayIcon?.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.Error("App", "Error during shutdown", ex);
        }

        AppLogger.Dispose();
        base.OnExit(e);
    }

    private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Error("App", "Unhandled exception", e.Exception);
        e.Handled = true;
    }
}
