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
