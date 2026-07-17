using System.Reflection;

namespace SmsNotificationService.Configuration;

public static class ConfigPathResolver
{
    private const string ConfigFileName = "appsettings.Production.json";
    private const string SubDir = "Munywele\\SmsNotificationService";

    public static string GetProgramDataDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), SubDir);

    public static string GetAppDir() =>
        Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? AppContext.BaseDirectory;

    public static string FindConfigFile()
    {
        var programDataPath = Path.Combine(GetProgramDataDir(), ConfigFileName);
        if (File.Exists(programDataPath))
            return programDataPath;

        var appDirPath = Path.Combine(GetAppDir(), ConfigFileName);
        if (File.Exists(appDirPath))
            return appDirPath;

        return programDataPath;
    }

    public static bool IsInProgramData(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var programDataDir = GetProgramDataDir();
        return path.StartsWith(programDataDir, StringComparison.OrdinalIgnoreCase);
    }
}
