using Comment.Application.Features.Privacy;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.API.AppInterfaces.Jobs;

public sealed class CommentUserErasureJob(
    IServiceScopeFactory scopeFactory,
    IOptions<CommentUserErasureOptions> options,
    ILogger<CommentUserErasureJob> logger) : BackgroundService
{
    private readonly string _workerId = CreateWorkerId();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.Zero;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (delay > TimeSpan.Zero)
            {
                try
                { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }

            using var jobExecution = CommomStartupExtensions.JobExecutionScope.Begin(
                logger,
                "comment-user-erasure");
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider
                    .GetRequiredService<ICommentUserErasureProcessor>()
                    .ProcessCycleAsync(_workerId, stoppingToken);
                delay = result.Claimed
                    ? result.Blocked ? options.Value.FailureDelay : options.Value.ActiveDelay
                    : options.Value.IdleDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Comment user-erasure cycle failed; durable fencing and checkpoints remain authoritative.");
                delay = options.Value.FailureDelay;
            }
        }
    }

    private static string CreateWorkerId()
    {
        var material = Encoding.UTF8.GetBytes(
            $"comment-erasure:{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}");
        return Convert.ToHexStringLower(SHA256.HashData(material));
    }
}
