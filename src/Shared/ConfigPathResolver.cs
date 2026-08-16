using System.Reflection;

namespace FeeSyncer.Shared;

public static class ConfigPathResolver
{
    public static string GetProgramDataDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Constants.SubDir);

    public static string GetAppDir() =>
        Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? AppContext.BaseDirectory;

    public static string GetMachineConfigFile() =>
        Path.Combine(GetProgramDataDir(), Constants.ConfigFileName);

    public static string GetMachineAgentConfigFile() =>
        Path.Combine(GetProgramDataDir(), "agentsettings.json");

    public static bool IsDevelopment()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    public static string GetActiveConfigFile() =>
        IsDevelopment() ? FindDevelopmentConfigFile() : GetMachineConfigFile();

    public static string GetActiveAgentConfigFile() =>
        IsDevelopment() ? FindDevelopmentAgentConfigFile() : GetMachineAgentConfigFile();

    public static void EnsureMachineAgentConfigFile()
    {
        var path = GetMachineAgentConfigFile();
        if (File.Exists(path)) return;

        Directory.CreateDirectory(GetProgramDataDir());
        File.WriteAllText(path, "{\n  \"Agent\": {}\n}\n");
    }

    public static void EnsureMachineConfigFiles()
    {
        var directory = GetProgramDataDir();
        Directory.CreateDirectory(directory);

        var smsPath = GetMachineConfigFile();
        if (!File.Exists(smsPath))
            File.WriteAllText(smsPath, "{}\n");

        EnsureMachineAgentConfigFile();
    }

    public static string FindConfigFile()
    {
        if (IsDevelopment())
            return FindDevelopmentConfigFile();

        var machinePath = GetMachineConfigFile();
        if (File.Exists(machinePath))
            return machinePath;

        foreach (var appDir in ApplicationDirectories())
        {
            var productionPath = Path.Combine(appDir, Constants.ConfigFileName);
            if (File.Exists(productionPath))
                return productionPath;

            var defaultPath = Path.Combine(appDir, "appsettings.json");
            if (File.Exists(defaultPath))
                return defaultPath;
        }

        return machinePath;
    }

    public static string FindAgentConfigFile()
    {
        if (IsDevelopment())
            return FindDevelopmentAgentConfigFile();

        var machinePath = GetMachineAgentConfigFile();
        if (File.Exists(machinePath))
            return machinePath;

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

        return machinePath;
    }

    private static string FindDevelopmentConfigFile()
    {
        foreach (var appDir in ApplicationDirectories())
        {
            var path = Path.Combine(appDir, "appsettings.Development.json");
            if (File.Exists(path))
                return path;

            path = Path.Combine(appDir, "Sms", "appsettings.Development.json");
            if (File.Exists(path))
                return path;

            path = Path.Combine(appDir, "appsettings.json");
            if (File.Exists(path))
                return path;

            path = Path.Combine(appDir, "Sms", "appsettings.json");
            if (File.Exists(path))
                return path;
        }

        return Path.Combine(GetAppDir(), "appsettings.Development.json");
    }

    private static string FindDevelopmentAgentConfigFile()
    {
        foreach (var appDir in ApplicationDirectories())
        {
            var path = Path.Combine(appDir, "Agent", "appsettings.Development.json");
            if (File.Exists(path))
                return path;

            path = Path.Combine(appDir, "appsettings.Development.json");
            if (File.Exists(path))
                return path;

            path = Path.Combine(appDir, "Agent", "appsettings.json");
            if (File.Exists(path))
                return path;

            path = Path.Combine(appDir, "appsettings.json");
            if (File.Exists(path))
                return path;
        }

        return Path.Combine(GetAppDir(), "Agent", "appsettings.Development.json");
    }

    private static IEnumerable<string> ApplicationDirectories()
    {
        var directory = new DirectoryInfo(GetAppDir());
        while (directory is not null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }

    public static string GetLogDir() => Path.Combine(GetProgramDataDir(), "logs");
}
