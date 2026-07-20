using Feed.Application.Features.Audience;
using Feed.Domain.AggregatesModel;
using MessageBrokerContracts.Following;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Infra.Persistence
{
    public sealed class RelationshipBootstrapProcessor(
        FeedContext context,
        IRelationshipBootstrapSource source,
        IRelationshipProjectionReconciler reconciler,
        IOptions<AudienceProjectionOptions> options,
        AudienceProjectionTelemetry telemetry,
        TimeProvider timeProvider) : IRelationshipBootstrapProcessor
    {
        private readonly AudienceProjectionOptions _options = options.Value;

        public async Task<RelationshipBootstrapPageResult> RunNextPageAsync(
            string workerId,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(workerId))
                throw new ArgumentException("A bootstrap worker ID is required.", nameof(workerId));
            var observed = NormalizeUtc(observedAtUtc);
            var state = await context.FeedAudienceBootstrapStates
                .SingleOrDefaultAsync(
                    item => item.Id == FeedAudienceBootstrapState.SingletonId,
                    cancellationToken);
            if (state is null)
            {
                state = FeedAudienceBootstrapState.Create(observed);
                await context.FeedAudienceBootstrapStates.AddAsync(state, cancellationToken);
            }

            var leaseToken = state.TryLease(
                workerId,
                observed,
                TimeSpan.FromSeconds(_options.BootstrapLeaseSeconds));
            if (!leaseToken.HasValue)
            {
                telemetry.RecordBootstrap("lease_busy");
                return new(false, state.CycleInProgress, false, state.NextCursor, "lease_busy");
            }

            state.BeginCycle(leaseToken.Value, observed);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                context.ChangeTracker.Clear();
                telemetry.RecordBootstrap("lease_race");
                return new(false, false, false, 0, "lease_race");
            }
            catch (DbUpdateException) when (context.Entry(state).State == EntityState.Added)
            {
                context.ChangeTracker.Clear();
                telemetry.RecordBootstrap("singleton_race");
                return new(false, false, false, 0, "singleton_race");
            }

            try
            {
                var page = await source.ReadPageAsync(
                    state.NextCursor,
                    _options.MaximumBootstrapPageSize,
                    state.SnapshotHighWatermark,
                    cancellationToken);
                ValidatePage(
                    page,
                    state.NextCursor,
                    state.SnapshotHighWatermark,
                    _options.MaximumBootstrapPageSize);

                await reconciler.ReconcilePageAsync(
                    page.Items,
                    Now(),
                    cancellationToken);

                // A malformed/gapped stream discovered while applying this page invalidates the
                // global negative-knowledge proof. Reconciliation must restart from cursor zero.
                if (state.Status == AudienceBootstrapStates.Degraded
                    || state.LeaseToken != leaseToken)
                {
                    telemetry.RecordBootstrap("projection_degraded");
                    return new(true, false, false, 0, "projection_degraded");
                }

                if (!page.HasMore)
                {
                    var hasUnresolvedGap = await context.FeedRelationshipCheckpoints
                        .AsNoTracking()
                        .AnyAsync(item => item.Status != AudienceProjectionStates.Healthy, cancellationToken);
                    var hasUnresolvedQuarantine = await context.FeedRelationshipEventQuarantines
                        .AsNoTracking()
                        .AnyAsync(item => item.ResolvedAtUtc == null, cancellationToken);
                    if (hasUnresolvedGap || hasUnresolvedQuarantine)
                    {
                        state.MarkDegraded("bootstrap_incomplete", Now());
                        await context.SaveChangesAsync(cancellationToken);
                        telemetry.RecordBootstrap("incomplete");
                        return new(true, false, false, state.NextCursor, "incomplete");
                    }
                }

                state.ApplyPage(
                    leaseToken.Value,
                    page.SnapshotHighWatermark,
                    page.SnapshotAsOfUtc,
                    page.NextCursor,
                    page.HasMore,
                    Now());
                await context.SaveChangesAsync(cancellationToken);
                telemetry.RecordBootstrap(page.HasMore ? "page_applied" : "completed");
                return new(
                    true,
                    page.HasMore,
                    !page.HasMore,
                    page.NextCursor,
                    page.HasMore ? "page_applied" : "completed");
            }
            catch (SnapshotValidationException)
            {
                if (state.LeaseToken == leaseToken)
                {
                    state.MarkDegraded("invalid_snapshot_page", Now());
                    await context.SaveChangesAsync(cancellationToken);
                }
                telemetry.RecordBootstrap("invalid_page");
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                if (state.LeaseToken == leaseToken
                    && state.LeaseExpiresAtUtc > Now())
                {
                    state.RecordFailure(leaseToken.Value, "bootstrap_page_failed", Now());
                    // The request token may already be canceled. Persist the
                    // degraded authority state with a short independent budget.
                    using var failureWriteTimeout = new CancellationTokenSource(
                        TimeSpan.FromSeconds(5));
                    await context.SaveChangesAsync(failureWriteTimeout.Token);
                }
                telemetry.RecordBootstrap("page_failed");
                throw;
            }
        }

        private static void ValidatePage(
            RelationshipSnapshotPage page,
            long requestedAfterCursor,
            long? requestedHighWatermark,
            int requestedPageSize)
        {
            if (requestedPageSize is < 1 or > 500
                || page is null
                || page.Items is null
                || page.Items.Count > requestedPageSize
                || page.SnapshotHighWatermark < 0
                || page.SnapshotAsOfUtc == default
                || page.NextCursor < requestedAfterCursor
                || page.NextCursor > page.SnapshotHighWatermark
                || (page.Items.Count == 0 && page.NextCursor != requestedAfterCursor)
                || (requestedHighWatermark.HasValue
                    && page.SnapshotHighWatermark != requestedHighWatermark.Value)
                || (page.HasMore && (page.Items.Count == 0
                    || page.NextCursor >= page.SnapshotHighWatermark))
                || (!page.HasMore && page.NextCursor != page.SnapshotHighWatermark))
            {
                throw new SnapshotValidationException();
            }

            var previous = requestedAfterCursor;
            foreach (var item in page.Items)
            {
                if (item is null
                    || item.Cursor <= previous
                    || item.Cursor > page.NextCursor
                    || item.Cursor > page.SnapshotHighWatermark)
                {
                    throw new SnapshotValidationException();
                }
                previous = item.Cursor;
            }
            if (page.Items.Count > 0 && page.Items[^1].Cursor != page.NextCursor)
                throw new SnapshotValidationException();
        }

        private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

        private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ when value == default => throw new ArgumentException("A timestamp is required."),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        private sealed class SnapshotValidationException : InvalidOperationException
        {
            public SnapshotValidationException()
                : base("Following returned an invalid or moving relationship snapshot page.") { }
        }
    }
}
