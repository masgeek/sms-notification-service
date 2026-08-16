namespace FeeSyncer.Shared;

public static class ExecutablePathResolver
{
    public static string? FindServiceExecutable(string executableName)
    {
        var folder = string.Equals(executableName, Constants.AgentExecutableName, StringComparison.OrdinalIgnoreCase)
            ? "Agent"
            : "Sms";
        var targetFramework = string.Equals(executableName, Constants.AgentExecutableName, StringComparison.OrdinalIgnoreCase)
            ? "net10.0"
            : "net10.0";

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, folder == "Agent" ? "..\\Agent" : "..", executableName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", folder, "bin", "Debug", targetFramework, executableName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", folder, "bin", "Release", targetFramework, executableName),
        };

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);
    }
}
