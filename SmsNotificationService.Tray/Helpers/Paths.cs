using System.IO;
using System.Reflection;

namespace SmsNotificationService.Tray.Helpers;

internal static class Paths
{
    private const string ConfigFileName = "appsettings.Production.json";
    private const string SubDir = "Munywele\\SmsNotificationService";

    public static string ProgramDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), SubDir);

    public static string AppDir =>
        Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? AppContext.BaseDirectory;

    public static string ConfigFile => FindConfigFile();

    public static string LogDir =>
        Path.Combine(ProgramDataDir, "logs");

    public static string ServiceExe =>
        Path.Combine(ProgramDataDir, "SmsNotificationService.exe");

    public static bool IsInProgramData(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return path.StartsWith(ProgramDataDir, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindConfigFile()
    {
        var programDataPath = Path.Combine(ProgramDataDir, ConfigFileName);
        if (File.Exists(programDataPath))
            return programDataPath;

        var appDirPath = Path.Combine(AppDir, ConfigFileName);
        if (File.Exists(appDirPath))
            return appDirPath;

        return programDataPath;
    }
}
