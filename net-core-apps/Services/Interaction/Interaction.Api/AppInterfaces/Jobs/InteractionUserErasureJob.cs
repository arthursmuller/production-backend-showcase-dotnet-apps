using System.Security.Cryptography;
using System.Text;
using Interaction.Application.Features.Privacy.Commands;
using Interaction.Application.Options;
using Microsoft.Extensions.Options;

namespace Interaction.Api.AppInterfaces.Jobs;

public sealed class InteractionUserErasureJob(
    IServiceScopeFactory scopeFactory,
    IOptions<InteractionOptions> options,
    ILogger<InteractionUserErasureJob> logger) : BackgroundService
{
    private readonly string _leaseOwner = CreateLeaseOwner();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(options.Value.ErasurePollSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            using var jobExecution = CommomStartupExtensions.JobExecutionScope.Begin(
                logger,
                "interaction-user-erasure");
            try
            {
                var changed = 0;
                do
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var command = scope.ServiceProvider.GetRequiredService<ProcessUserErasureCommand>();
                    changed = await command.Handle(_leaseOwner, stoppingToken);
                    if (changed > 0)
                    {
                        logger.LogInformation(
                            "Interaction erasure processed {RecordCount} bounded records.",
                            changed);
                    }
                }
                while (changed > 0 && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                InteractionJobSafeLogging.LogFailure(logger, "user-erasure", exception);
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
                break;
        }
    }

    private static string CreateLeaseOwner()
    {
        var material = Encoding.UTF8.GetBytes(
            $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}");
        return Convert.ToHexStringLower(SHA256.HashData(material));
    }
}
