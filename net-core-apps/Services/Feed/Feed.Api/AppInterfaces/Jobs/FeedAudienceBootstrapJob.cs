using Feed.Application.Features.Audience;
using Feed.Infra.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.API.AppInterfaces.Jobs;

/// <summary>
/// Advances one bounded, fixed-high-watermark Following snapshot page per scope. Progress and
/// completeness are durable, so restarts and competing job replicas cannot manufacture negative
/// relationship knowledge.
/// </summary>
public sealed class FeedAudienceBootstrapJob(
    IServiceScopeFactory scopeFactory,
    IOptions<AudienceProjectionOptions> options,
    TimeProvider timeProvider,
    ILogger<FeedAudienceBootstrapJob> logger) : BackgroundService
{
    private readonly AudienceProjectionOptions _options = options.Value;
    private readonly string _workerId =
        $"feed-audience:{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.Zero;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, timeProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            using var jobExecution = CommomStartupExtensions.JobExecutionScope.Begin(
                logger,
                "feed-audience-bootstrap");
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider
                    .GetRequiredService<IRelationshipBootstrapProcessor>()
                    .RunNextPageAsync(
                        _workerId,
                        timeProvider.GetUtcNow().UtcDateTime,
                        stoppingToken);
                delay = result.Completed
                    ? TimeSpan.FromSeconds(_options.BootstrapIntervalSeconds)
                    : result.Acquired && result.HasMore
                        ? TimeSpan.FromMilliseconds(_options.BootstrapPageDelayMilliseconds)
                        : TimeSpan.FromSeconds(_options.BootstrapRetrySeconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Feed audience bootstrap page failed; the durable fixed-boundary cursor was preserved.");
                delay = TimeSpan.FromSeconds(_options.BootstrapRetrySeconds);
            }
        }
    }
}
