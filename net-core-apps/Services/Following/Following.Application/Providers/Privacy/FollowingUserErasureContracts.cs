using System;
using System.Threading;
using System.Threading.Tasks;
using Inspacy.Privacy;

namespace Following.Application.Providers.Privacy
{
    public sealed record FollowingUserErasureRequest(
        Guid EventId,
        Guid RequestId,
        int UserId,
        long ErasureVersion,
        DateTime RequestedAtUtc);

    public sealed record FollowingUserErasureScheduleResult(
        bool Scheduled,
        bool Superseded,
        long EffectiveVersion);

    public sealed record FollowingUserErasureCycleResult(
        bool Claimed,
        bool Completed,
        int Batches,
        int Rows,
        bool LeaseLost);

    public interface IFollowingUserErasureScheduler
    {
        Task<FollowingUserErasureScheduleResult> ScheduleAsync(
            FollowingUserErasureRequest request,
            CancellationToken cancellationToken);
    }

    public interface IFollowingUserErasureProcessor
    {
        Task<FollowingUserErasureCycleResult> ProcessCycleAsync(
            string workerId,
            CancellationToken cancellationToken);
    }

    public sealed class FollowingUserErasureOptions
    {
        public const string SectionName = "FollowingUserErasure";

        public int BatchSize { get; set; } = 100;
        public int MaxBatchesPerCycle { get; set; } = 8;
        public int CycleTimeBudgetMilliseconds { get; set; } = 5_000;
        public int LeaseSeconds { get; set; } = 30;
        public int ActiveDelayMilliseconds { get; set; } = 100;
        public int IdleDelayMilliseconds { get; set; } = 2_000;
        public int FailureDelayMilliseconds { get; set; } = 5_000;
        public int MaxPendingAgeMinutes { get; set; } = 15;

        public TimeSpan CycleTimeBudget =>
            TimeSpan.FromMilliseconds(CycleTimeBudgetMilliseconds);
        public TimeSpan LeaseDuration => TimeSpan.FromSeconds(LeaseSeconds);
        public TimeSpan ActiveDelay => TimeSpan.FromMilliseconds(ActiveDelayMilliseconds);
        public TimeSpan IdleDelay => TimeSpan.FromMilliseconds(IdleDelayMilliseconds);
        public TimeSpan FailureDelay => TimeSpan.FromMilliseconds(FailureDelayMilliseconds);

        public static bool IsValid(FollowingUserErasureOptions options) =>
            options is not null
            && UserErasureWorkerPolicy.IsValid(
                new UserErasureWorkerSettings(
                    options.BatchSize,
                    options.MaxBatchesPerCycle,
                    options.CycleTimeBudgetMilliseconds,
                    options.LeaseSeconds,
                    options.ActiveDelayMilliseconds,
                    options.IdleDelayMilliseconds,
                    options.FailureDelayMilliseconds,
                    options.MaxPendingAgeMinutes),
                new UserErasureWorkerLimits(
                    MaximumBatchSize: 200,
                    MinimumLeaseSeconds: 5));
    }
}
