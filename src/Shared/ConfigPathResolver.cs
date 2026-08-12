using System.Reflection;

namespace FeeSyncer.Shared;

public static class ConfigPathResolver
{
    public static string GetProgramDataDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Constants.SubDir);

    public static string GetAppDir() =>
        Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? AppContext.BaseDirectory;

    public static string FindConfigFile()
    {
        foreach (var appDir in ApplicationDirectories())
        {
            var productionPath = Path.Combine(appDir, Constants.ConfigFileName);
            if (File.Exists(productionPath))
                return productionPath;

            var defaultPath = Path.Combine(appDir, "appsettings.json");
            if (File.Exists(defaultPath))
                return defaultPath;
        }

        var programDataPath = Path.Combine(GetProgramDataDir(), Constants.ConfigFileName);
        if (File.Exists(programDataPath))
            return programDataPath;

        return Path.Combine(GetAppDir(), Constants.ConfigFileName);
    }

    public static string FindAgentConfigFile()
    {
        foreach (var appDir in ApplicationDirectories())
        {
            var agentDir = Path.Combine(appDir, "Agent");
            var productionPath = Path.Combine(agentDir, Constants.ConfigFileName);
            if (File.Exists(productionPath))
                return productionPath;

            var defaultPath = Path.Combine(agentDir, "appsettings.json");
            if (File.Exists(defaultPath))
                return defaultPath;
        }

        return Path.Combine(GetAppDir(), "Agent", Constants.ConfigFileName);
    }

    private static IEnumerable<string> ApplicationDirectories()
    {
        var appDir = GetAppDir();
        yield return appDir;

        var parentDir = Directory.GetParent(appDir)?.FullName;
        if (!string.IsNullOrWhiteSpace(parentDir) && !string.Equals(parentDir, appDir, StringComparison.OrdinalIgnoreCase))
            yield return parentDir;
    }

    public static string GetLogDir() => Path.Combine(GetProgramDataDir(), "logs");
}
