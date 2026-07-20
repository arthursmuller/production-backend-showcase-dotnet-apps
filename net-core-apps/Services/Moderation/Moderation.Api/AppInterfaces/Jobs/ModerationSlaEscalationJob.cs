using Microsoft.Extensions.Options;
using Moderation.Application.Features.Cases.Commands;
using Moderation.Application.Features.Cases.Queries;
using Moderation.Application.Options;

namespace Moderation.API.AppInterfaces.Jobs;

public sealed class ModerationSlaEscalationJob(
    IServiceScopeFactory scopeFactory,
    IOptions<ModerationEscalationOptions> options,
    ILogger<ModerationSlaEscalationJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.PollIntervalSeconds));
        do
        {
            using var jobExecution = CommomStartupExtensions.JobExecutionScope.Begin(
                logger,
                "moderation-sla-escalation");
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var command = scope.ServiceProvider.GetRequiredService<EscalateOverdueCasesCommand>();
                var changed = await command.ExecuteAsync(stoppingToken);
                if (changed > 0)
                    logger.LogInformation("Moderation SLA worker escalated {Count} bounded cases.", changed);
                var backlog = await scope.ServiceProvider
                    .GetRequiredService<GetModerationSlaBacklogQuery>()
                    .ExecuteAsync(stoppingToken);
                if (backlog.OverdueCount > 0)
                {
                    logger.LogWarning(
                        "Moderation has {OverdueCount} active overdue cases; oldest overdue age is {OldestAgeSeconds:F0}s.",
                        backlog.OverdueCount,
                        backlog.OldestOverdueAgeSeconds);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                ModerationSafeLogging.LogSlaWorkerFailure(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public static class ModerationSafeLogging
{
    public static void LogSlaWorkerFailure(ILogger logger, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);
        // Do not attach the exception or its message: providers can embed protected connection details.
        logger.LogError(
            "Moderation SLA worker failed with exception type {ExceptionType}.",
            exception.GetType().Name);
    }

    public static void LogRetentionWorkerFailure(ILogger logger, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);
        // Restricted identifiers, SQL text, connection details, and evidence references
        // must never be attached to retention failure logs.
        logger.LogError(
            "Moderation retention worker failed with exception type {ExceptionType}.",
            exception.GetType().Name);
    }

    public static void LogErasureWorkerFailure(ILogger logger, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);
        logger.LogError(
            "Moderation erasure worker failed with exception type {ExceptionType}.",
            exception.GetType().Name);
    }
}
