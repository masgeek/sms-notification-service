using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed class FeeProcessorUpdateWorker(
    IOptions<AgentOptions> options,
    ILogger<FeeProcessorUpdateWorker> logger,
    IHostEnvironment environment) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.Value.FeeProcessorUpdateEnabled)
            {
                try
                {
                    await RunUpdateAsync(stoppingToken);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Fee-processor automatic update failed.");
                }
            }

            await Task.Delay(TimeSpan.FromHours(Math.Max(1, options.Value.FeeProcessorUpdateIntervalHours)), stoppingToken);
        }
    }

    private async Task RunUpdateAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.FeeProcessorPath)
            || string.IsNullOrWhiteSpace(settings.FeeProcessorRepository))
        {
            logger.LogWarning("Fee-processor updates are enabled but path or repository is not configured.");
            return;
        }

        var script = Path.Combine(environment.ContentRootPath, "scripts", "Update-FeeProcessor.ps1");
        if (!File.Exists(script))
        {
            logger.LogError("Fee-processor updater script was not found at {ScriptPath}.", script);
            return;
        }

        var arguments = new List<string> { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
            "-AppPath", settings.FeeProcessorPath, "-Repository", settings.FeeProcessorRepository, "-BackupRoot", settings.FeeProcessorBackupPath };
        arguments.AddRange(string.IsNullOrWhiteSpace(settings.FeeProcessorTag)
            ? ["-Branch", string.IsNullOrWhiteSpace(settings.FeeProcessorBranch) ? "main" : settings.FeeProcessorBranch]
            : ["-Tag", settings.FeeProcessorTag]);

        logger.LogInformation("Starting fee-processor update from {Repository}.", settings.FeeProcessorRepository);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = settings.FeeProcessorPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (!string.IsNullOrWhiteSpace(output)) logger.LogInformation("Fee-processor updater: {Output}", output.Trim());
        if (process.ExitCode != 0) throw new InvalidOperationException($"Updater exited with code {process.ExitCode}: {error.Trim()}");
    }

}
