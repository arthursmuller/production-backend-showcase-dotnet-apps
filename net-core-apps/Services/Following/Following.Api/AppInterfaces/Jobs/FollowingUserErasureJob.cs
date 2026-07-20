using Following.Application.Providers.Privacy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Following.API.AppInterfaces.Jobs
{
    public sealed class FollowingUserErasureJob(
        IServiceScopeFactory scopeFactory,
        IOptions<FollowingUserErasureOptions> configuredOptions,
        TimeProvider timeProvider,
        ILogger<FollowingUserErasureJob> logger) : BackgroundService
    {
        private readonly FollowingUserErasureOptions _options = configuredOptions.Value;
        private readonly string _workerId = BuildWorkerId();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = _options.IdleDelay;
                using var jobExecution = CommomStartupExtensions.JobExecutionScope.Begin(
                    logger,
                    "following-user-erasure");
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var processor = scope.ServiceProvider
                        .GetRequiredService<IFollowingUserErasureProcessor>();
                    var result = await processor.ProcessCycleAsync(_workerId, stoppingToken);
                    if (result.Claimed)
                    {
                        delay = _options.ActiveDelay;
                        logger.LogInformation(
                            "Following privacy erasure cycle: batches={Batches}, rows={Rows}, " +
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
                    logger.LogError(exception, "Following privacy erasure cycle failed.");
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
            if (machine.Length > 72)
                machine = machine[..72];
            return $"following-erasure-{machine}-{Guid.NewGuid():N}";
        }
    }
}
