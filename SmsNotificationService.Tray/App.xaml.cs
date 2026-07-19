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
            TrayLogger.Info($"SmsNotificationService Tray starting (v{VersionHelper.GetCurrentVersion()})");
            TrayLogger.Info($"OS: {Environment.OSVersion}, .NET: {Environment.Version}");

            base.OnStartup(e);

            _trayIcon = new TrayIcon();

            TrayLogger.Info("Tray app initialized successfully");
        }
        catch (Exception ex)
        {
            TrayLogger.Error("Fatal error during startup", ex);
            MessageBox.Show($"Startup failed: {ex.Message}", "SmsNotificationService Tray", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            TrayLogger.Info("Tray app shutting down");
            _trayIcon?.Dispose();
        }
        catch (Exception ex)
        {
            TrayLogger.Error("Error during shutdown", ex);
        }

        TrayLogger.Dispose();
        base.OnExit(e);
    }

    private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        TrayLogger.Error("Unhandled exception", e.Exception);
        e.Handled = true;
    }
}
