using Comment.Application.Features.Visibility;
using Comment.Domain.AggregatesModel.Visibility;
using MessageBrokerContracts.Following;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Persistence.Visibility
{
    public sealed class CommentRelationshipBootstrapProcessor(
        CommentContext context,
        ICommentRelationshipSnapshotSource source,
        ICommentRelationshipProjectionReconciler reconciler,
        IOptions<CommentAudienceProjectionOptions> options,
        CommentAudienceProjectionTelemetry telemetry,
        TimeProvider timeProvider) : ICommentRelationshipBootstrapProcessor
    {
        private readonly CommentAudienceProjectionOptions _options = options.Value;

        public async Task<CommentRelationshipBootstrapPageResult> RunNextPageAsync(
            string workerId, DateTime observedAtUtc, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(workerId))
                throw new ArgumentException("A relationship bootstrap worker id is required.");
            var observed = Utc(observedAtUtc);
            var state = await context.CommentAudienceProjectionStates.SingleOrDefaultAsync(
                item => item.Id == CommentAudienceProjectionState.SingletonId, cancellationToken);
            if (state is null)
            {
                state = CommentAudienceProjectionState.Create(observed);
                await context.CommentAudienceProjectionStates.AddAsync(state, cancellationToken);
            }
            var token = state.TryLease(workerId, observed,
                TimeSpan.FromSeconds(_options.BootstrapLeaseSeconds));
            if (!token.HasValue)
            {
                telemetry.Bootstrap("lease_busy");
                return new(false, state.CycleInProgress, false, state.NextCursor, "lease_busy");
            }
            state.BeginCycle(token.Value, observed);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                context.ChangeTracker.Clear();
                telemetry.Bootstrap("lease_busy");
                return new(false, false, false, 0, "lease_race");
            }

            try
            {
                var page = await source.ReadPageAsync(state.NextCursor, _options.SnapshotPageSize,
                    state.SnapshotHighWatermark, cancellationToken);
                ValidatePage(page, state.NextCursor, state.SnapshotHighWatermark,
                    _options.SnapshotPageSize);
                await reconciler.ReconcilePageAsync(page.Items, Now(), cancellationToken);

                if (!page.HasMore)
                {
                    var unhealthy = await context.CommentRelationshipCheckpoints.AsNoTracking()
                        .AnyAsync(item => item.Status != CommentProjectionStates.Healthy, cancellationToken);
                    var quarantine = await context.CommentRelationshipEventQuarantines.AsNoTracking()
                        .AnyAsync(item => item.ResolvedAtUtc == null, cancellationToken);
                    if (unhealthy || quarantine)
                    {
                        state.Degrade("bootstrap_incomplete", Now());
                        await context.SaveChangesAsync(cancellationToken);
                        telemetry.Bootstrap("failed");
                        return new(true, false, false, state.NextCursor, "incomplete");
                    }
                }

                state.ApplyPage(token.Value, page.SnapshotHighWatermark, page.SnapshotAsOfUtc,
                    page.NextCursor, page.HasMore, Now());
                await context.SaveChangesAsync(cancellationToken);
                telemetry.Bootstrap(page.HasMore ? "page" : "complete");
                return new(true, page.HasMore, !page.HasMore, page.NextCursor,
                    page.HasMore ? "page_applied" : "completed");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                state.Degrade("bootstrap_page_failed", Now());
                // The request token may already be canceled. Persist the
                // fail-closed state with a short independent budget.
                using var failureWriteTimeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(5));
                await context.SaveChangesAsync(failureWriteTimeout.Token);
                telemetry.Bootstrap("failed");
                throw;
            }
        }

        private static void ValidatePage(RelationshipSnapshotPage page, long requestedCursor,
            long? requestedHighWatermark, int maximumPageSize)
        {
            if (page is null || page.Items is null
                || page.Items.Count > maximumPageSize || page.SnapshotHighWatermark < 0
                || page.SnapshotAsOfUtc == default || page.NextCursor < requestedCursor
                || page.NextCursor > page.SnapshotHighWatermark
                || (requestedHighWatermark.HasValue
                    && requestedHighWatermark != page.SnapshotHighWatermark)
                || (page.HasMore && (page.Items.Count == 0
                    || page.NextCursor >= page.SnapshotHighWatermark))
                || (!page.HasMore && page.NextCursor != page.SnapshotHighWatermark))
                throw new InvalidOperationException("Following returned an invalid or moving snapshot page.");
            var cursor = requestedCursor;
            foreach (var item in page.Items)
            {
                if (item is null || item.Cursor <= cursor || item.Cursor > page.NextCursor
                    || item.Cursor > page.SnapshotHighWatermark)
                    throw new InvalidOperationException("Following returned an invalid snapshot cursor.");
                cursor = item.Cursor;
            }
            if (page.Items.Count > 0 && page.Items[^1].Cursor != page.NextCursor)
                throw new InvalidOperationException("Following returned a discontinuous snapshot page.");
        }

        private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
        private static DateTime Utc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ when value == default => throw new ArgumentException("A timestamp is required."),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
