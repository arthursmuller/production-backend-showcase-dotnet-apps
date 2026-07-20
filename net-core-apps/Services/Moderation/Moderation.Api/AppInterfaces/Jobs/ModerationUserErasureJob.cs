using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Moderation.Application.Features.Privacy.Commands;
using Moderation.Application.Options;

namespace Moderation.API.AppInterfaces.Jobs;

public sealed class ModerationUserErasureJob(
    IServiceScopeFactory scopeFactory,
    IOptions<ModerationErasureOptions> options,
    ILogger<ModerationUserErasureJob> logger) : BackgroundService
{
    private readonly string _leaseOwner = CreateLeaseOwner();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation(
                "Moderation erasure cleanup is disabled; tombstones still reject subject writes.");
            return;
        }

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(settings.PollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            using var jobExecution = CommomStartupExtensions.JobExecutionScope.Begin(
                logger,
                "moderation-user-erasure");
            try
            {
                int workUnits;
                do
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var command = scope.ServiceProvider
                        .GetRequiredService<ProcessModerationUserErasureCommand>();
                    workUnits = await command.Handle(_leaseOwner, stoppingToken);
                    if (workUnits > 0)
                    {
                        logger.LogInformation(
                            "Moderation erasure processed {WorkUnitCount} bounded work units.",
                            workUnits);
                    }
                }
                while (workUnits > 0 && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                ModerationSafeLogging.LogErasureWorkerFailure(logger, exception);
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
                break;
        }
    }

    private static string CreateLeaseOwner()
    {
        var source =
            $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }
}
