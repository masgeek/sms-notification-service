using System.Reflection;

namespace SmsNotificationService.Tray;

internal static class VersionHelper
{
    public static string GetCurrentVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version?.ToString(3) ?? "0.0.0";
    }
}
