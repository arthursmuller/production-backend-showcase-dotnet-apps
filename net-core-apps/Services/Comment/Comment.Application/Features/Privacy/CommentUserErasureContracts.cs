using MessageBrokerContracts.Privacy.Restricted;
using Inspacy.Privacy;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.Features.Privacy;

public sealed class CommentUserErasureOptions
{
    public const string SectionName = "CommentUserErasure";

    public int BatchSize { get; set; } = 50;
    public int MaximumRowsPerCycle { get; set; } = 400;
    public int MaximumBatchesPerCycle { get; set; } = 12;
    public int CycleTimeBudgetMilliseconds { get; set; } = 4_000;
    public int LeaseSeconds { get; set; } = 30;
    public int ActiveDelayMilliseconds { get; set; } = 100;
    public int IdleDelayMilliseconds { get; set; } = 2_000;
    public int FailureDelayMilliseconds { get; set; } = 5_000;
    public int MaximumPendingAgeMinutes { get; set; } = 15;

    public TimeSpan CycleTimeBudget => TimeSpan.FromMilliseconds(CycleTimeBudgetMilliseconds);
    public TimeSpan LeaseDuration => TimeSpan.FromSeconds(LeaseSeconds);
    public TimeSpan ActiveDelay => TimeSpan.FromMilliseconds(ActiveDelayMilliseconds);
    public TimeSpan IdleDelay => TimeSpan.FromMilliseconds(IdleDelayMilliseconds);
    public TimeSpan FailureDelay => TimeSpan.FromMilliseconds(FailureDelayMilliseconds);

    public static bool IsValid(CommentUserErasureOptions value) =>
        value is not null
        && UserErasureWorkerPolicy.IsValid(
            new UserErasureWorkerSettings(
                value.BatchSize,
                value.MaximumBatchesPerCycle,
                value.CycleTimeBudgetMilliseconds,
                value.LeaseSeconds,
                value.ActiveDelayMilliseconds,
                value.IdleDelayMilliseconds,
                value.FailureDelayMilliseconds,
                value.MaximumPendingAgeMinutes),
            new UserErasureWorkerLimits(
                MaximumBatchSize: 100,
                MinimumLeaseSeconds: 10))
        && value.MaximumRowsPerCycle is >= 1 and <= 2_000
        && value.MaximumRowsPerCycle >= value.BatchSize;
}

public sealed record CommentUserErasureScheduleResult(
    bool Scheduled,
    bool Duplicate,
    bool Superseded,
    long EffectiveVersion);

public sealed record CommentUserErasureCycleResult(
    bool Claimed,
    bool Completed,
    bool Blocked,
    bool LeaseLost,
    int Batches,
    int Rows);

public interface ICommentUserMutationScope : IAsyncDisposable
{
    bool Allowed { get; }
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface ICommentUserErasureAuthority
{
    Task<ICommentUserMutationScope> AcquireMutationAsync(
        IReadOnlyCollection<int> userIds,
        CancellationToken cancellationToken);

    Task<bool> IsErasedAsync(int userId, CancellationToken cancellationToken);

    Task<IReadOnlySet<int>> GetErasedAsync(
        IReadOnlyCollection<int> userIds,
        CancellationToken cancellationToken);
}

public interface ICommentUserErasureScheduler
{
    Task<CommentUserErasureScheduleResult> ScheduleAsync(
        UserErasureRequested request,
        DateTime observedAtUtc,
        CancellationToken cancellationToken);
}

public interface ICommentUserErasureProcessor
{
    Task<CommentUserErasureCycleResult> ProcessCycleAsync(
        string workerId,
        CancellationToken cancellationToken);
}

public interface ICommentPrivacyEventPublisher
{
    Task StageCompletionAsync(
        UserErasureCompleted message,
        CancellationToken cancellationToken);
}

public sealed class ScheduleCommentUserErasureCommand(
    ICommentUserErasureScheduler scheduler,
    TimeProvider timeProvider)
{
    public Task<CommentUserErasureScheduleResult> Handle(
        UserErasureRequested request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        if (request.EventId == Guid.Empty
            || request.RequestId == Guid.Empty
            || request.UserId is <= 0 or > int.MaxValue
            || request.ErasureVersion <= 0
            || request.RequestedAtUtc < DateTimeOffset.UnixEpoch
            || request.RequestedAtUtc > now.AddMinutes(5))
            throw new Domain.Exceptions.CommentException("The user erasure request is invalid.");

        return scheduler.ScheduleAsync(request, now.UtcDateTime, cancellationToken);
    }
}
