using System.Diagnostics.Metrics;
using Inspacy.Observability;

namespace Following.Infra.Persistence
{
    public sealed class FollowingUserErasureMetrics
    {
        private static readonly Counter<long> ScheduledCounter =
            PlatformInstrumentation.Meter.CreateCounter<long>("following.user_erasure.scheduled");
        private static readonly Counter<long> SupersededCounter =
            PlatformInstrumentation.Meter.CreateCounter<long>("following.user_erasure.superseded");
        private static readonly Counter<long> ClaimedCounter =
            PlatformInstrumentation.Meter.CreateCounter<long>("following.user_erasure.claimed");
        private static readonly Counter<long> RowsCounter =
            PlatformInstrumentation.Meter.CreateCounter<long>("following.user_erasure.rows_processed");
        private static readonly Counter<long> CompletedCounter =
            PlatformInstrumentation.Meter.CreateCounter<long>("following.user_erasure.completed");
        private static readonly Counter<long> LeaseLostCounter =
            PlatformInstrumentation.Meter.CreateCounter<long>("following.user_erasure.lease_lost");
        private static readonly Counter<long> FailuresCounter =
            PlatformInstrumentation.Meter.CreateCounter<long>("following.user_erasure.failures");

        public void Scheduled() => ScheduledCounter.Add(1);
        public void Superseded() => SupersededCounter.Add(1);
        public void Claimed() => ClaimedCounter.Add(1);
        public void Rows(int count) { if (count > 0) RowsCounter.Add(count); }
        public void Completed() => CompletedCounter.Add(1);
        public void LeaseLost() => LeaseLostCounter.Add(1);
        public void Failure() => FailuresCounter.Add(1);
    }
}
