using Interaction.Application.Features.Maintenance.Commands;
using Interaction.Application.Features.Maintenance.Queries;
using Interaction.Application.Options;
using Microsoft.Extensions.Options;

namespace Interaction.Api.AppInterfaces.Jobs;

public sealed class InteractionCounterReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IOptions<InteractionOptions> options,
    ILogger<InteractionCounterReconciliationJob> logger) : BackgroundService
{
    private readonly InteractionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var jobExecution = CommomStartupExtensions.JobExecutionScope.Begin(
                logger,
                "interaction-counter-reconciliation");
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var command = scope.ServiceProvider.GetRequiredService<ReconcileCountersCommand>();
                var corrected = await command.Handle(_options.ReconciliationBatchSize, stoppingToken);
                if (corrected > 0)
                    logger.LogWarning("Reconciled {SummaryCount} drifted engagement summaries.", corrected);
                var gapBacklog = await scope.ServiceProvider
                    .GetRequiredService<GetProjectionGapBacklogQuery>()
                    .Handle(stoppingToken);
                if (gapBacklog.OldestAgeSeconds > 60)
                {
                    logger.LogWarning(
                        "Interaction has {GapCount} unresolved projection gaps; oldest age is {OldestAgeSeconds:F0}s.",
                        gapBacklog.PendingCount,
                        gapBacklog.OldestAgeSeconds);
                }
                var revisionRetention = await scope.ServiceProvider
                    .GetRequiredService<PruneInteractionAuthorityRevisionsCommand>()
                    .ExecuteAsync(stoppingToken);
                if (revisionRetention.PrunedRevisionCount > 0)
                {
                    logger.LogInformation(
                        "Pruned {RevisionCount} superseded interaction authority revisions in a bounded batch.",
                        revisionRetention.PrunedRevisionCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                InteractionJobSafeLogging.LogFailure(logger, "counter-reconciliation", exception);
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.ReconciliationPollSeconds), stoppingToken);
        }
    }
}
