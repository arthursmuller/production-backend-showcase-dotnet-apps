using Feed.Application.Dtos;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Application.Features.Engagement
{
    public enum EngagementProjectionOutcome
    {
        Applied,
        StaleOrDuplicate,
        GapRetained,
        GapRecovered,
        InvalidQuarantined,
    }

    public sealed record EngagementProjectionResult(
        EngagementProjectionOutcome Outcome,
        string ResourceType,
        string ResourceId,
        long LastAppliedVersion,
        long HighestObservedVersion,
        string Status);

    /// <summary>
    /// Feed-owned asynchronous projection port. Implementations must never call Interaction
    /// synchronously and must make the write durable before returning.
    /// </summary>
    public interface IEngagementSummaryProjectionWriter
    {
        Task<EngagementProjectionResult> ApplyAsync(
            EngagementSummaryProjectionDto summary,
            DateTime observedAtUtc,
            CancellationToken cancellationToken);
    }

    public sealed record ProjectEngagementSummaryCommand(
        IEngagementSummaryProjectionWriter projection,
        TimeProvider timeProvider)
        : Command<EngagementSummaryProjectionDto, EngagementProjectionResult>
    {
        public override Task<EngagementProjectionResult> Handle(
            EngagementSummaryProjectionDto request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            return projection.ApplyAsync(
                request,
                timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);
        }
    }
}
