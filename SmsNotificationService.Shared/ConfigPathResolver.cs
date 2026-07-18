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
        var appDirPath = Path.Combine(GetAppDir(), Constants.ConfigFileName);
        if (File.Exists(appDirPath))
            return appDirPath;

        var programDataPath = Path.Combine(GetProgramDataDir(), Constants.ConfigFileName);
        if (File.Exists(programDataPath))
            return programDataPath;

        return appDirPath;
    }

    public static string GetLogDir() => Path.Combine(GetProgramDataDir(), "logs");
}
