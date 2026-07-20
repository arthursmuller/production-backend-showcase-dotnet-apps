using Comment.Application.Features.Visibility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.API.AppInterfaces.Jobs
{
    public sealed class CommentAudienceBootstrapJob(
        IServiceScopeFactory scopeFactory,
        IOptions<CommentAudienceProjectionOptions> options,
        TimeProvider timeProvider,
        ILogger<CommentAudienceBootstrapJob> logger) : BackgroundService
    {
        private readonly CommentAudienceProjectionOptions _options = options.Value;
        private readonly string _workerId = $"comment-audience:{Environment.MachineName}:{Guid.NewGuid():N}";

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var delay = TimeSpan.Zero;
            while (!stoppingToken.IsCancellationRequested)
            {
                if (delay > TimeSpan.Zero)
                {
                    try
                    { await Task.Delay(delay, timeProvider, stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                }
                using var jobExecution = CommomStartupExtensions.JobExecutionScope.Begin(
                    logger,
                    "comment-audience-bootstrap");
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var result = await scope.ServiceProvider
                        .GetRequiredService<ICommentRelationshipBootstrapProcessor>()
                        .RunNextPageAsync(_workerId, timeProvider.GetUtcNow().UtcDateTime, stoppingToken);
                    delay = result.Completed
                        ? TimeSpan.FromSeconds(_options.BootstrapIntervalSeconds)
                        : result.Acquired && result.HasMore
                            ? TimeSpan.FromMilliseconds(_options.BootstrapPageDelayMilliseconds)
                            : TimeSpan.FromSeconds(_options.BootstrapRetrySeconds);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    logger.LogError(exception,
                        "Comment audience snapshot page failed; the durable cursor remains fail closed.");
                    delay = TimeSpan.FromSeconds(_options.BootstrapRetrySeconds);
                }
            }
        }
    }
}
