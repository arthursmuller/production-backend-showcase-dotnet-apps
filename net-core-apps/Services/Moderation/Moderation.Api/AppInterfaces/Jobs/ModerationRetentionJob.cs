using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Moderation.Application.Features.Maintenance.Commands;
using Moderation.Application.Options;

namespace Moderation.API.AppInterfaces.Jobs;

public sealed class ModerationRetentionJob(
    IServiceScopeFactory scopeFactory,
    IOptions<ModerationRetentionOptions> options,
    ILogger<ModerationRetentionJob> logger) : BackgroundService
{
    private readonly string _leaseOwner = CreateLeaseOwner();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation(
                "Moderation case retention is disabled; decision revision retention remains active.");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.PollIntervalSeconds));
        do
        {
            using var jobExecution = CommomStartupExtensions.JobExecutionScope.Begin(
                logger,
                "moderation-retention");
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                if (settings.Enabled)
                {
                    var command = scope.ServiceProvider.GetRequiredService<RunModerationRetentionCommand>();
                    var result = await command.ExecuteAsync(_leaseOwner, stoppingToken);
                    if (result.LeaseAcquired && (result.CasesRedacted > 0 || result.CasesPurged > 0))
                    {
                        logger.LogInformation(
                            "Moderation retention completed bounded batch: {RedactedCount} redacted and {PurgedCount} purged.",
                            result.CasesRedacted,
                            result.CasesPurged);
                    }
                }
                var revisionRetention = await scope.ServiceProvider
                    .GetRequiredService<PruneModerationDecisionRevisionsCommand>()
                    .ExecuteAsync(stoppingToken);
                if (revisionRetention.PrunedRevisionCount > 0)
                {
                    logger.LogInformation(
                        "Pruned {RevisionCount} superseded moderation decision revisions in a bounded batch.",
                        revisionRetention.PrunedRevisionCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                ModerationSafeLogging.LogRetentionWorkerFailure(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static string CreateLeaseOwner()
    {
        var source = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }
}
