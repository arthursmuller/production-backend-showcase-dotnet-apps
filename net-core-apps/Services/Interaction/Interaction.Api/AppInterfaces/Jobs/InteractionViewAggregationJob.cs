using Interaction.Application.Features.Maintenance.Commands;
using Interaction.Application.Options;
using Microsoft.Extensions.Options;

namespace Interaction.Api.AppInterfaces.Jobs;

public sealed class InteractionViewAggregationJob(
    IServiceScopeFactory scopeFactory,
    IOptions<InteractionOptions> options,
    ILogger<InteractionViewAggregationJob> logger) : BackgroundService
{
    private readonly InteractionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var jobExecution = CommomStartupExtensions.JobExecutionScope.Begin(
                logger,
                "interaction-view-aggregation");
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var command = scope.ServiceProvider.GetRequiredService<AggregateViewsCommand>();
                var processed = await command.Handle(_options.ViewAggregationBatchSize, stoppingToken);
                if (processed > 0)
                    logger.LogInformation("Aggregated {ObservationCount} bounded view observations.", processed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                InteractionJobSafeLogging.LogFailure(logger, "view-aggregation", exception);
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.ViewAggregationPollSeconds), stoppingToken);
        }
    }
}

public static class InteractionJobSafeLogging
{
    public static void LogFailure(ILogger logger, string jobName, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);
        logger.LogError(
            "Interaction {JobName} iteration failed with exception type {ExceptionType}.",
            jobName,
            exception.GetType().Name);
    }
}
