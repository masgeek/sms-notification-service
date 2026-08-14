using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using FeeSyncer.Shared;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed class FeeProcessorUpdateWorker(
    IOptions<AgentOptions> options,
    ILogger<FeeProcessorUpdateWorker> logger) : BackgroundService
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

        var php = FeeProcessorToolResolver.Resolve(settings.PhpExecutablePath, "php");
        var composer = FeeProcessorToolResolver.Resolve(settings.ComposerExecutablePath, "composer");
        if (string.IsNullOrWhiteSpace(php) || string.IsNullOrWhiteSpace(composer))
        {
            logger.LogWarning("Fee-processor update requires PHP and Composer. Configure executable paths or add them to PATH.");
            return;
        }

        var request = new FeeProcessorDeploymentRequest(
            settings.FeeProcessorPath,
            settings.FeeProcessorRepository,
            string.IsNullOrWhiteSpace(settings.FeeProcessorBranch) ? "main" : settings.FeeProcessorBranch,
            settings.FeeProcessorTag.Trim(),
            settings.FeeProcessorBackupPath,
            php,
            composer,
            settings.FeeProcessorSshUsername,
            settings.FeeProcessorSshKeyPath,
            settings.FeeProcessorSshPassphrase);
        await new FeeProcessorDeploymentRunner().RunAsync(
            request,
            message => logger.LogInformation("Fee-processor update: {Message}", message),
            cancellationToken);
    }

}
