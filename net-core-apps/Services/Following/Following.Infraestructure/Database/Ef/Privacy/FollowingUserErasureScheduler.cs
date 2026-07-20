using Following.Application.Providers.Privacy;
using Following.Application.Providers.Time;
using Following.Domain.AggregatesModel;
using Following.Domain.Repositories;
using Following.Infra.Providers;
using MessageBrokerContracts.Privacy.Restricted;
using MessageBrokerCore.Abstractions;
using MessageBrokerExtensions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Infra.Persistence
{
    public sealed class FollowingUserErasureScheduler(
        FollowingContext context,
        IFollowingRepository repository,
        IIntegrationMessagePublisher publisher,
        IClock clock,
        FollowingUserErasureMetrics metrics) : IFollowingUserErasureScheduler
    {
        public async Task<FollowingUserErasureScheduleResult> ScheduleAsync(
            FollowingUserErasureRequest request,
            CancellationToken cancellationToken)
        {
            Validate(request);
            var nowUtc = NormalizeUtc(clock.UtcNow);
            await using var authority = await repository.AcquireUserErasureMutation(
                request.UserId,
                cancellationToken);
            var tombstone = await context.UserErasures
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(item => item.UserId == request.UserId, cancellationToken);

            if (tombstone is null)
            {
                tombstone = FollowingUserErasure.Create(
                    request.UserId,
                    request.RequestId,
                    request.ErasureVersion,
                    request.RequestedAtUtc,
                    nowUtc);
                await context.UserErasures.AddAsync(tombstone, cancellationToken);
                await repository.SaveChanges(cancellationToken);
                await authority.CommitAsync(cancellationToken);
                metrics.Scheduled();
                return new FollowingUserErasureScheduleResult(true, false, request.ErasureVersion);
            }

            if (request.ErasureVersion < tombstone.ErasureVersion
                || (request.ErasureVersion == tombstone.ErasureVersion
                    && request.RequestId != tombstone.RequestId))
            {
                await PublishAcknowledgementAsync(
                    request.RequestId,
                    request.UserId,
                    request.ErasureVersion,
                    UserErasureOutcomes.Superseded,
                    nowUtc,
                    cancellationToken);
                await repository.SaveChanges(cancellationToken);
                await authority.CommitAsync(cancellationToken);
                metrics.Superseded();
                return new FollowingUserErasureScheduleResult(
                    false,
                    true,
                    tombstone.ErasureVersion);
            }

            if (request.ErasureVersion == tombstone.ErasureVersion)
            {
                await authority.CommitAsync(cancellationToken);
                return new FollowingUserErasureScheduleResult(
                    false,
                    false,
                    tombstone.ErasureVersion);
            }

            if (tombstone.Status != FollowingUserErasureStatus.Completed)
            {
                await PublishAcknowledgementAsync(
                    tombstone.RequestId,
                    tombstone.UserId,
                    tombstone.ErasureVersion,
                    UserErasureOutcomes.Superseded,
                    nowUtc,
                    cancellationToken);
            }

            tombstone.ScheduleNewer(
                request.RequestId,
                request.ErasureVersion,
                request.RequestedAtUtc,
                nowUtc);
            await repository.SaveChanges(cancellationToken);
            await authority.CommitAsync(cancellationToken);
            metrics.Scheduled();
            return new FollowingUserErasureScheduleResult(true, false, request.ErasureVersion);
        }

        private async Task PublishAcknowledgementAsync(
            Guid requestId,
            int userId,
            long erasureVersion,
            string outcome,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            var eventId = FollowingUserErasureEventIds.Create(
                requestId,
                erasureVersion,
                outcome);
            await context.PublishToBusOutboxAsync(
                publisher,
                new UserErasureCompleted
                {
                    EventId = eventId,
                    RequestId = requestId,
                    UserId = userId,
                    ErasureVersion = erasureVersion,
                    Service = "following",
                    Outcome = outcome,
                    CompletedAtUtc = new DateTimeOffset(nowUtc, TimeSpan.Zero),
                },
                eventId,
                cancellationToken);
        }

        private static void Validate(FollowingUserErasureRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.EventId == Guid.Empty || request.RequestId == Guid.Empty)
                throw new ArgumentException("Stable privacy event and request IDs are required.", nameof(request));
            if (request.UserId <= 0 || request.ErasureVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(request));
            if (request.RequestedAtUtc == default)
                throw new ArgumentException("A privacy request timestamp is required.", nameof(request));
        }

        private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }

    internal static class FollowingUserErasureEventIds
    {
        public static Guid Create(Guid requestId, long version, string outcome)
        {
            var input = Encoding.UTF8.GetBytes(
                $"following-user-erasure|{requestId:D}|{version}|{outcome}");
            var hash = SHA256.HashData(input);
            return new Guid(hash.AsSpan(0, 16));
        }
    }
}
