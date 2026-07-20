using Comment.Application.Features.Media.Commands;
using Comment.Application.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.API.AppInterfaces.Jobs;

/// <summary>
/// Bounded durable-media reconciler. Concrete discovery registers this service
/// for JobWorker and Combined roles; no JobsExtensions convention is used.
/// </summary>
public sealed class CommentMediaReconciliationJob(
    IServiceScopeFactory scopeFactory,
    ILogger<CommentMediaReconciliationJob> logger,
    CommentMediaIngestionOptions options,
    TimeProvider timeProvider) : BackgroundService
{
    private readonly string _workerId = BuildWorkerId();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextCleanupAtUtc = UtcNow;
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = options.IdleDelay;
            using var jobExecution = CommomStartupExtensions.JobExecutionScope.Begin(
                logger,
                "comment-media-reconciliation");
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var cycle = scope.ServiceProvider
                    .GetRequiredService<CommentMediaReconciliationCycle>();
                var result = await cycle.ExecuteAsync(_workerId, stoppingToken);
                if (result.Claimed > 0)
                {
                    logger.LogInformation(
                        "Reconciled durable Comment media batches: claimed={Claimed}, " +
                        "completed={Completed}, retried={Retried}, compensated={Compensated}.",
                        result.Claimed,
                        result.Completed,
                        result.Retried,
                        result.Compensated);
                    delay = options.ActiveDelay;
                }

                if (UtcNow >= nextCleanupAtUtc)
                {
                    var purged = await cycle.PurgeAsync(stoppingToken);
                    if (purged > 0)
                    {
                        logger.LogInformation(
                            "Purged {Count} retained terminal Comment media-ingestion batches.",
                            purged);
                    }
                    nextCleanupAtUtc = purged == options.ReconciliationBatchSize
                        ? UtcNow
                        : UtcNow.Add(options.CleanupInterval);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Comment media reconciliation cycle failed.");
                delay = options.FailureDelay;
            }

            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    private static string BuildWorkerId()
    {
        var machine = Environment.MachineName;
        if (machine.Length > 80)
            machine = machine[..80];
        return $"media-{machine}-{Guid.NewGuid():N}";
    }
}
