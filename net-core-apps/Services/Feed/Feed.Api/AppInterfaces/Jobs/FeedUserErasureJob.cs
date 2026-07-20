using Feed.Application.Features.Privacy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.API.AppInterfaces.Jobs;

public sealed class FeedUserErasureJob(
    IServiceScopeFactory scopeFactory,
    IOptions<FeedUserErasureOptions> configuredOptions,
    TimeProvider timeProvider,
    ILogger<FeedUserErasureJob> logger) : BackgroundService
{
    private readonly FeedUserErasureOptions _options = configuredOptions.Value;
    private readonly string _workerId = BuildWorkerId();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = _options.IdleDelay;
            using var jobExecution = CommomStartupExtensions.JobExecutionScope.Begin(
                logger,
                "feed-user-erasure");
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider
                    .GetRequiredService<IFeedUserErasureProcessor>()
                    .ProcessCycleAsync(_workerId, stoppingToken);
                if (result.Claimed)
                {
                    delay = _options.ActiveDelay;
                    logger.LogInformation(
                        "Feed privacy erasure cycle: batches={Batches}, rows={Rows}, " +
                        "completed={Completed}, leaseLost={LeaseLost}.",
                        result.Batches,
                        result.Rows,
                        result.Completed,
                        result.LeaseLost);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Feed privacy erasure cycle failed.");
                delay = _options.FailureDelay;
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

    private static string BuildWorkerId()
    {
        var machine = Environment.MachineName;
        if (machine.Length > 80)
            machine = machine[..80];
        return $"feed-erasure-{machine}-{Guid.NewGuid():N}";
    }
}
