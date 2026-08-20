using System.Diagnostics;
using System.ServiceProcess;
using LibGit2Sharp;

namespace FeeSyncer.Shared;

public sealed record FeeProcessorDeploymentRequest(
    string AppPath,
    string Repository,
    string Branch,
    string Tag,
    string BackupRoot,
    string PhpPath,
    string ComposerPath,
    string SshUsername = "git",
    string SshKeyPath = "",
    string SshPassphrase = "",
    string IisSiteName = "FeeProcessor",
    IReadOnlyList<string>? WindowsServices = null,
    string GitExecutablePath = "");

public sealed class FeeProcessorDeploymentRunner
{
    private readonly FeeProcessorGitUpdater gitUpdater = new();

    public async Task RunAsync(FeeProcessorDeploymentRequest request, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        Directory.CreateDirectory(request.AppPath);
        var services = request.WindowsServices ?? ["FeeProcessorQueue"];
        var appCmd = FindAppCmd();
        var php = FeeProcessorToolResolver.Resolve(request.PhpPath, "php");
        if (string.IsNullOrWhiteSpace(php))
            throw new InvalidOperationException("PHP must be installed and available on PATH or configured explicitly.");
        var composer = FeeProcessorToolResolver.Resolve(request.ComposerPath, "composer");
        if (string.IsNullOrWhiteSpace(composer))
            throw new InvalidOperationException("Composer must be installed and available on PATH or configured explicitly.");
        var backup = CreateBackup(request, progress);
        try
        {
            foreach (var service in services)
                if (ServiceInstalled(service))
                    await RunAsync("sc.exe", ["stop", service], request.AppPath, message => Report(progress, message), cancellationToken, false);
                else Report(progress, $"Service {service} is not installed; skipping stop.");

            if (!string.IsNullOrWhiteSpace(appCmd))
                await RunAsync(appCmd, ["stop", "site", $"/{request.IisSiteName}"], request.AppPath, message => Report(progress, message), cancellationToken, false);
            gitUpdater.Update(new FeeProcessorGitRequest(request.AppPath, request.Repository, request.Branch, request.Tag, request.SshUsername, request.SshKeyPath, request.SshPassphrase, request.GitExecutablePath), message => Report(progress, message));
            await InstallNodeDependenciesAsync(request.AppPath, progress, cancellationToken);
            // await RunAsync(composer, ["install", "--no-dev", "--optimize-autoloader", "--no-interaction", "-vvv"], request.AppPath, message => Report(progress, message), cancellationToken);
            await RunAsync(composer, ["install", "--optimize-autoloader", "--no-interaction", "-vvv"], request.AppPath, message => Report(progress, message), cancellationToken);
            await RunAsync(php, ["artisan", "migrate", "--force"], request.AppPath, message => Report(progress, message), cancellationToken);
            foreach (var command in new[] { "optimize:clear", "config:cache", "route:cache", "view:cache", "event:cache" })
                await RunAsync(php, ["artisan", command], request.AppPath, message => Report(progress, message), cancellationToken);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(appCmd))
                await RunAsync(appCmd, ["start", "site", $"/{request.IisSiteName}"], request.AppPath, message => Report(progress, message), CancellationToken.None, false);
            foreach (var service in services)
                if (ServiceInstalled(service))
                    await RunAsync("sc.exe", ["start", service], request.AppPath, message => Report(progress, message), CancellationToken.None, false);
        }

        progress?.Invoke($"Backup created at {backup}.");
    }

    private static async Task InstallNodeDependenciesAsync(
        string appPath,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        var pnpm = FeeProcessorToolResolver.Resolve(string.Empty, "pnpm", message => Report(progress, message));
        if (string.IsNullOrWhiteSpace(pnpm))
        {
            Report(progress, "pnpm was not found; skipping Node dependency installation and build, then continuing the update.");
            return;
        }

        await RunOptionalPnpmCommandAsync(pnpm, "install", appPath, progress, cancellationToken);
        await RunOptionalPnpmCommandAsync(pnpm, "build", appPath, progress, cancellationToken);
    }

    private static async Task RunOptionalPnpmCommandAsync(
        string pnpm,
        string command,
        string appPath,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var exitCode = await RunAsync(
                pnpm,
                [command],
                appPath,
                message => Report(progress, message),
                cancellationToken,
                failOnError: false);
            if (exitCode != 0)
                Report(progress, $"pnpm {command} exited with code {exitCode}; continuing the update.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Report(progress, $"pnpm {command} could not complete: {exception.Message} Continuing the update.");
        }
    }

    private static string CreateBackup(FeeProcessorDeploymentRequest request, Action<string>? progress)
    {
        var root = string.IsNullOrWhiteSpace(request.BackupRoot) ? Path.Combine(request.AppPath, "backups") : request.BackupRoot;
        var backup = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backup);
        using var repository = Repository.IsValid(request.AppPath) ? new Repository(request.AppPath) : null;
        var previousCommit = repository?.Head.Tip.Sha ?? "No previous Git checkout.";
        File.WriteAllText(Path.Combine(backup, "previous-commit.txt"), previousCommit);
        var env = Path.Combine(request.AppPath, ".env");
        if (File.Exists(env))
        {
            var envBackup = Path.Combine(backup, ".env");
            File.Copy(env, envBackup, true);
            Report(progress, $"Ignored .env preserved and backed up at {envBackup}.");
        }
        Report(progress, $"Backup prepared at {backup}.");
        return backup;
    }

    private static void ValidateRequest(FeeProcessorDeploymentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Repository)) throw new InvalidOperationException("Repository is required.");
    }

    private static string FindAppCmd()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var path = Path.Combine(windows, "System32", "inetsrv", "appcmd.exe");
        return File.Exists(path) ? path : string.Empty;
    }

    private static bool ServiceInstalled(string name)
    {
        try { return ServiceController.GetServices().Any(service => service.ServiceName.Equals(name, StringComparison.OrdinalIgnoreCase)); }
        catch { return false; }
    }

    private static void Report(Action<string>? progress, string message)
    {
        FeeProcessorActivityLogger.Write(message);
        AppLogger.Info("FeeProcessorUpdate", message);
        progress?.Invoke(message);
    }


    private static async Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory,
        Action<string>? progress, CancellationToken cancellationToken, bool failOnError = true)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            }
        };
        progress?.Invoke($"Running {fileName} {string.Join(" ", arguments)}");
        var extension = Path.GetExtension(fileName);
        var usePowerShell = OperatingSystem.IsWindows() && extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase);
        var useWindowsShell = OperatingSystem.IsWindows() && !usePowerShell
            && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
        if (usePowerShell)
        {
            process.StartInfo.FileName = "powershell.exe";
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(fileName);
            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        }
        else if (useWindowsShell)
        {
            process.StartInfo.FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
            process.StartInfo.ArgumentList.Add("/c");
            process.StartInfo.ArgumentList.Add(string.Join(" ", new[] { Quote(fileName) }.Concat(arguments.Select(Quote))));
        }
        else
        {
            process.StartInfo.FileName = fileName;
            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        }
        process.Start();
        var outputTask = ReadOutputAsync(process.StandardOutput, progress, cancellationToken);
        var errorTask = ReadOutputAsync(process.StandardError, progress, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(outputTask, errorTask);
        if (failOnError && process.ExitCode != 0)
            throw new InvalidOperationException($"{fileName} exited with code {process.ExitCode}. Review the preceding output for the server response and authentication details.");
        return process.ExitCode;
    }

    private static string Quote(string value) => value.Contains(' ')
        ? $"\"{value.Replace("\"", "\\\"")}\""
        : value;

    private static async Task ReadOutputAsync(StreamReader reader, Action<string>? progress, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
            if (!string.IsNullOrWhiteSpace(line)) progress?.Invoke(line);
    }
}
