using Feed.Application.Features.Feeds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.API.AppInterfaces.Jobs;

public sealed class FeedMaintenanceJob(
    IServiceScopeFactory scopeFactory,
    IOptions<FeedOptions> options,
    TimeProvider timeProvider,
    ILogger<FeedMaintenanceJob> logger) : BackgroundService
{
    private readonly FeedOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var jobExecution = CommomStartupExtensions.JobExecutionScope.Begin(
                logger,
                "feed-maintenance");
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider
                    .GetRequiredService<IFeedMaintenanceProcessor>();
                var now = timeProvider.GetUtcNow().UtcDateTime;
                await processor.CompactAsync(now, stoppingToken);
                await processor.ReplayAsync(now, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Feed maintenance cycle failed; the durable cursor was preserved.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_options.MaintenanceIntervalSeconds),
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
