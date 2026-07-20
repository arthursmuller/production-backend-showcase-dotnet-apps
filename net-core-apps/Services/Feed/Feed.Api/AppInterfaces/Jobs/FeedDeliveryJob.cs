using Feed.Application.Features.Feeds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.API.AppInterfaces.Jobs;

/// <summary>
/// Privacy revocations always receive the first bounded claim in a cycle. A fresh scope is
/// used per cycle so failed units cannot leak tracked EF state into later lease attempts.
/// </summary>
public sealed class FeedDeliveryJob(
    IServiceScopeFactory scopeFactory,
    IOptions<FeedOptions> options,
    TimeProvider timeProvider,
    ILogger<FeedDeliveryJob> logger) : BackgroundService
{
    private readonly FeedOptions _options = options.Value;
    private readonly string _workerId =
        $"feed:{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            using var jobExecution = CommomStartupExtensions.JobExecutionScope.Begin(
                logger,
                "feed-delivery");
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var now = timeProvider.GetUtcNow().UtcDateTime;
                processed += await scope.ServiceProvider
                    .GetRequiredService<IFeedRevocationProcessor>()
                    .ProcessBatchAsync(_workerId, now, stoppingToken);
                processed += await scope.ServiceProvider
                    .GetRequiredService<IFeedFanoutProcessor>()
                    .ProcessBatchAsync(_workerId, now, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Feed delivery cycle failed; leased work will resume after expiry.");
            }

            if (processed > 0)
            {
                await Task.Yield();
                continue;
            }
            try
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(_options.WorkerIntervalMilliseconds),
                    timeProvider,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
