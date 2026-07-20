using MessageBrokerContracts.Privacy.Restricted;
using Inspacy.Privacy;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Application.Features.Privacy;

public sealed class FeedUserErasureOptions
{
    public const string SectionName = "FeedUserErasure";

    public int BatchSize { get; set; } = 100;
    public int MaximumRowsPerCycle { get; set; } = 800;
    public int MaximumBatchesPerCycle { get; set; } = 16;
    public int CycleTimeBudgetMilliseconds { get; set; } = 5_000;
    public int LeaseSeconds { get; set; } = 30;
    public int ActiveDelayMilliseconds { get; set; } = 100;
    public int IdleDelayMilliseconds { get; set; } = 2_000;
    public int FailureDelayMilliseconds { get; set; } = 5_000;
    public int MaximumPendingAgeMinutes { get; set; } = 15;
    public int CacheExpirySafetySeconds { get; set; } = 2;

    public TimeSpan CycleTimeBudget => TimeSpan.FromMilliseconds(CycleTimeBudgetMilliseconds);
    public TimeSpan LeaseDuration => TimeSpan.FromSeconds(LeaseSeconds);
    public TimeSpan ActiveDelay => TimeSpan.FromMilliseconds(ActiveDelayMilliseconds);
    public TimeSpan IdleDelay => TimeSpan.FromMilliseconds(IdleDelayMilliseconds);
    public TimeSpan FailureDelay => TimeSpan.FromMilliseconds(FailureDelayMilliseconds);

    public static bool IsValid(FeedUserErasureOptions value) =>
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
                MaximumBatchSize: 200,
                MinimumLeaseSeconds: 10))
        && value.MaximumRowsPerCycle is >= 1 and <= 4_000
        && value.MaximumRowsPerCycle >= value.BatchSize
        && value.CacheExpirySafetySeconds is >= 0 and <= 30;
}

public sealed record FeedUserErasureScheduleResult(
    bool Scheduled,
    bool Duplicate,
    bool Superseded,
    long EffectiveVersion);

public sealed record FeedUserErasureCycleResult(
    bool Claimed,
    bool Completed,
    bool LeaseLost,
    int Batches,
    int Rows);

public interface IFeedUserMutationScope : IAsyncDisposable
{
    bool Allowed { get; }
    IReadOnlySet<int> ErasedUserIds { get; }
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IFeedUserErasureAuthority
{
    Task<IFeedUserMutationScope> AcquireMutationAsync(
        IReadOnlyCollection<int> userIds,
        CancellationToken cancellationToken);

    Task<bool> IsErasedAsync(int userId, CancellationToken cancellationToken);

    Task<IReadOnlySet<int>> GetErasedAsync(
        IReadOnlyCollection<int> userIds,
        CancellationToken cancellationToken);
}

public interface IFeedUserErasureScheduler
{
    Task<FeedUserErasureScheduleResult> ScheduleAsync(
        UserErasureRequested request,
        DateTime observedAtUtc,
        CancellationToken cancellationToken);
}

public interface IFeedUserErasureProcessor
{
    Task<FeedUserErasureCycleResult> ProcessCycleAsync(
        string workerId,
        CancellationToken cancellationToken);
}
