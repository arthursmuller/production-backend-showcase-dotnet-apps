using Following.Application.Providers.Relationships;
using Following.Application.Providers.Time;
using Following.Domain.AggregatesModel;
using Following.Infra.Providers;
using MassTransit;
using MessageBrokerContracts.Following;
using MessageBrokerCore.Abstractions;
using MessageBrokerExtensions;
using Microsoft.EntityFrameworkCore;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Infra.Persistence
{
    public sealed class RelationshipEventWriter(
        FollowingContext context,
        IIntegrationMessagePublisher publisher,
        IClock clock) : IRelationshipEventWriter
    {
        public async Task<RelationshipStateChanged> StageAsync(
            RelationshipTransition transition,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(transition);
            Validate(transition);
            cancellationToken.ThrowIfCancellationRequested();

            var kind = transition.RelationshipKind.Trim().ToLowerInvariant();
            var state = transition.State.Trim().ToLowerInvariant();
            var occurredAtUtc = NormalizeUtc(clock.UtcNow);
            var eventId = NewId.NextGuid();
            // Erasure tombstones hide streams from public/read models immediately, but the
            // processor must still advance the durable stream to a final revoked version.
            var stream = await context.RelationshipEventStreams
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                item => item.OwnerUserId == transition.OwnerUserId
                    && item.ViewerUserId == transition.ViewerUserId
                    && item.RelationshipKind == kind,
                cancellationToken);

            long streamVersion;
            if (stream is null)
            {
                stream = RelationshipEventStream.Create(
                    NewId.NextGuid(),
                    eventId,
                    transition.OwnerUserId,
                    transition.ViewerUserId,
                    kind,
                    state,
                    occurredAtUtc);
                await context.RelationshipEventStreams.AddAsync(stream, cancellationToken);
                streamVersion = stream.CurrentVersion;
            }
            else
            {
                streamVersion = stream.Advance(eventId, state, occurredAtUtc);
            }

            var contract = new RelationshipStateChanged
            {
                EventId = eventId,
                StreamId = stream.StreamId,
                StreamVersion = streamVersion,
                OwnerUserId = transition.OwnerUserId,
                ViewerUserId = transition.ViewerUserId,
                RelationshipKind = kind,
                State = state,
                OccurredAtUtc = occurredAtUtc,
                ReasonCode = NormalizeReason(transition.ReasonCode),
            };

            await context.PublishToBusOutboxAsync(
                publisher,
                contract,
                eventId,
                cancellationToken);
            return contract;
        }

        private static void Validate(RelationshipTransition transition)
        {
            var kind = transition.RelationshipKind?.Trim().ToLowerInvariant();
            var state = transition.State?.Trim().ToLowerInvariant();
            if (transition.OwnerUserId <= 0 || transition.ViewerUserId <= 0)
                throw new ArgumentOutOfRangeException(nameof(transition));
            if (transition.OwnerUserId == transition.ViewerUserId)
                throw new ArgumentException("Relationship transitions must be directional.");
            if (!RelationshipKinds.IsSupported(kind))
                throw new ArgumentException("Unsupported relationship kind.", nameof(transition));
            if (!RelationshipStates.IsSupported(state))
                throw new ArgumentException("Unsupported relationship state.", nameof(transition));
            if (kind == RelationshipKinds.Block
                && state == RelationshipStates.Active)
            {
                throw new ArgumentException("Block streams must use blocked or revoked state.");
            }
            if (kind != RelationshipKinds.Block
                && state == RelationshipStates.Blocked)
            {
                throw new ArgumentException("Blocked state is valid only for block streams.");
            }
        }

        private static string NormalizeReason(string reason)
        {
            var normalized = reason?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 64)
                throw new ArgumentException("A bounded reason code is required.", nameof(reason));
            return normalized;
        }

        private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }

    public sealed class RelationshipSnapshotReader(FollowingContext context)
        : IRelationshipSnapshotReader
    {
        public async Task<RelationshipSnapshotPage> ReadPageAsync(
            long afterCursor,
            int pageSize,
            long? snapshotHighWatermark,
            CancellationToken cancellationToken)
        {
            if (afterCursor < 0)
                throw new ArgumentOutOfRangeException(nameof(afterCursor));
            if (pageSize is < 1 or > RelationshipSnapshotLimits.MaximumPageSize)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            if (snapshotHighWatermark is < 0)
                throw new ArgumentOutOfRangeException(nameof(snapshotHighWatermark));
            if (afterCursor > 0 && !snapshotHighWatermark.HasValue)
            {
                throw new ArgumentException(
                    "A continuation page requires the first page's snapshot high watermark.",
                    nameof(snapshotHighWatermark));
            }

            var authorityHighWatermark = await context.RelationshipEventStreams
                .AsNoTracking()
                .MaxAsync(stream => (long?)stream.Id, cancellationToken)
                ?? 0;
            if (snapshotHighWatermark > authorityHighWatermark)
                throw new ArgumentOutOfRangeException(nameof(snapshotHighWatermark));
            if (afterCursor == 0
                && snapshotHighWatermark.HasValue
                && snapshotHighWatermark.Value != authorityHighWatermark)
            {
                throw new ArgumentException(
                    "A first page must capture the current authority high watermark.",
                    nameof(snapshotHighWatermark));
            }
            var highWatermark = snapshotHighWatermark ?? authorityHighWatermark;
            if (afterCursor > highWatermark)
                throw new ArgumentOutOfRangeException(nameof(afterCursor));
            // Creation time of the fixed boundary row is immutable and therefore remains stable
            // across every page in the cycle. Live events carry newer stream versions separately.
            var snapshotAsOfUtc = highWatermark == 0
                ? DateTime.UnixEpoch
                : NormalizeUtc(await context.RelationshipEventStreams
                        .AsNoTracking()
                        .Where(stream => stream.Id == highWatermark)
                        .Select(stream => stream.CreatedAtUtc)
                        .SingleAsync(cancellationToken));

            var rows = await context.RelationshipEventStreams
                .AsNoTracking()
                .Where(stream => stream.Id > afterCursor && stream.Id <= highWatermark)
                .OrderBy(stream => stream.Id)
                .Take(pageSize + 1)
                .ToListAsync(cancellationToken);
            var hasMore = rows.Count > pageSize;
            if (hasMore)
                rows.RemoveAt(rows.Count - 1);

            return new RelationshipSnapshotPage
            {
                Items = rows.Select(stream => new RelationshipStateSnapshot
                {
                    Cursor = stream.Id,
                    StreamId = stream.StreamId,
                    LastEventId = stream.LastEventId,
                    StreamVersion = stream.CurrentVersion,
                    OwnerUserId = stream.OwnerUserId,
                    ViewerUserId = stream.ViewerUserId,
                    RelationshipKind = stream.RelationshipKind,
                    State = stream.CurrentState,
                    AsOfUtc = NormalizeUtc(stream.LastOccurredAtUtc),
                }).ToArray(),
                NextCursor = rows.Count == 0 ? afterCursor : rows[^1].Id,
                HasMore = hasMore,
                SnapshotHighWatermark = highWatermark,
                SnapshotAsOfUtc = snapshotAsOfUtc,
            };
        }

        private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
