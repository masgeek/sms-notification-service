using System.Reflection;

namespace SmsNotificationService.Shared;

public static class ConfigPathResolver
{
    public static string GetProgramDataDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Constants.SubDir);

    public static string GetAppDir() =>
        Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? AppContext.BaseDirectory;

    public static string FindConfigFile()
    {
        var programDataPath = Path.Combine(GetProgramDataDir(), Constants.ConfigFileName);
        if (File.Exists(programDataPath))
            return programDataPath;

        var appDirPath = Path.Combine(GetAppDir(), Constants.ConfigFileName);
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

    public static string GetLogDir() => Path.Combine(GetProgramDataDir(), "logs");
}
