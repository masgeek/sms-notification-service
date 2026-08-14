using System.Diagnostics;

namespace FeeSyncer.Shared;

public static class FeeProcessorToolResolver
{
    public static string Resolve(string configuredPath, string commandName, Action<string>? progress = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            progress?.Invoke($"Running {configuredPath} --version");
            return TestExecutable(configuredPath, progress) ? configuredPath : string.Empty;
        }

        // Prefer the executable resolved by the current service environment.
        progress?.Invoke($"Running {commandName} --version from PATH");
        if (TestExecutable(commandName, progress))
            return commandName;

        foreach (var lookupCommand in new[] { "where.exe", "which" })
        {
            try
            {
                progress?.Invoke($"Running {lookupCommand} {commandName}");
                using var lookup = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = lookupCommand,
                        Arguments = commandName,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    }
                };
                lookup.Start();
                var output = lookup.StandardOutput.ReadToEnd();
                lookup.WaitForExit(5000);
                foreach (var path in output.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    progress?.Invoke($"Running {path} --version");
                    if (TestExecutable(path, progress)) return path;
                }
            }
            catch
            {
                // Try the next lookup command.
            }
        }

        return string.Empty;
    }

    private static bool TestExecutable(string path, Action<string>? progress = null)
    {
        try
        {
            var useWindowsShell = OperatingSystem.IsWindows()
                && !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase);
            var usePowerShell = OperatingSystem.IsWindows()
                && string.Equals(Path.GetExtension(path), ".ps1", StringComparison.OrdinalIgnoreCase);
            useWindowsShell = useWindowsShell && !usePowerShell;
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = useWindowsShell ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe" : path,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };
            if (usePowerShell)
            {
                process.StartInfo.FileName = "powershell.exe";
                process.StartInfo.ArgumentList.Add("-NoProfile");
                process.StartInfo.ArgumentList.Add("-File");
                process.StartInfo.ArgumentList.Add(path);
                process.StartInfo.ArgumentList.Add("--version");
            }
            else if (useWindowsShell)
            {
                process.StartInfo.ArgumentList.Add("/c");
                process.StartInfo.ArgumentList.Add($"{ShellToken(path)} --version");
            }
            else
            {
                process.StartInfo.ArgumentList.Add("--version");
            }

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);
            if (!string.IsNullOrWhiteSpace(output)) progress?.Invoke(output.Trim());
            if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error)) progress?.Invoke(error.Trim());
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string ShellToken(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
}
