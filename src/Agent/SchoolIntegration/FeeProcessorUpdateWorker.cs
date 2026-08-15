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

            var settings = options.Value;
            var interval = FeeProcessorInterval.TryParse(settings.FeeProcessorUpdateInterval, out var parsed)
                ? parsed
                : TimeSpan.FromHours(Math.Max(1, settings.FeeProcessorUpdateIntervalHours));
            await Task.Delay(interval, stoppingToken);
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
            settings.FeeProcessorSshPassphrase,
            GitExecutablePath: settings.GitExecutablePath);
        await new FeeProcessorDeploymentRunner().RunAsync(
            request,
            message => logger.LogInformation("Fee-processor update: {Message}", message),
            cancellationToken);
    }

}
