using Feed.Application.Features.Privacy;
using Feed.Domain.AggregatesModel;
using Feed.Infra.Providers;
using MessageBrokerContracts.Privacy.Restricted;
using MessageBrokerCore.Abstractions;
using MessageBrokerExtensions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Infra.Persistence;

public sealed class FeedUserErasureScheduler(
    FeedContext context,
    FeedUserErasureAuthority authority,
    IIntegrationMessagePublisher publisher,
    FeedUserErasureMetrics metrics) : IFeedUserErasureScheduler
{
    public async Task<FeedUserErasureScheduleResult> ScheduleAsync(
        UserErasureRequested request,
        DateTime observedAtUtc,
        CancellationToken cancellationToken)
    {
        Validate(request, observedAtUtc);
        var userId = checked((int)request.UserId);
        var now = NormalizeUtc(observedAtUtc);
        await using var mutation = await authority.AcquireErasureMutationAsync(userId, cancellationToken);
        var tombstone = await context.FeedUserErasures
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        if (tombstone is null)
        {
            tombstone = FeedUserErasure.Create(
                userId,
                request.RequestId,
                request.EventId,
                request.ErasureVersion,
                request.RequestedAtUtc.UtcDateTime,
                now);
            await context.FeedUserErasures.AddAsync(tombstone, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await mutation.CommitAsync(cancellationToken);
            metrics.Scheduled();
            return new(true, false, false, request.ErasureVersion);
        }

        if (request.ErasureVersion < tombstone.ErasureVersion
            || (request.ErasureVersion == tombstone.ErasureVersion
                && request.RequestId != tombstone.RequestId))
        {
            await PublishAcknowledgementAsync(
                request.RequestId,
                userId,
                request.ErasureVersion,
                UserErasureOutcomes.Superseded,
                now,
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await mutation.CommitAsync(cancellationToken);
            metrics.Superseded();
            return new(false, false, true, tombstone.ErasureVersion);
        }

        if (request.ErasureVersion == tombstone.ErasureVersion)
        {
            await mutation.CommitAsync(cancellationToken);
            return new(false, true, false, tombstone.ErasureVersion);
        }

        if (tombstone.Status != FeedUserErasureStatus.Completed)
        {
            await PublishAcknowledgementAsync(
                tombstone.RequestId,
                tombstone.UserId,
                tombstone.ErasureVersion,
                UserErasureOutcomes.Superseded,
                now,
                cancellationToken);
        }

        tombstone.ScheduleNewer(
            request.RequestId,
            request.EventId,
            request.ErasureVersion,
            request.RequestedAtUtc.UtcDateTime,
            now);
        await context.SaveChangesAsync(cancellationToken);
        await mutation.CommitAsync(cancellationToken);
        metrics.Scheduled();
        return new(true, false, false, request.ErasureVersion);
    }

    private async Task PublishAcknowledgementAsync(
        Guid requestId,
        int userId,
        long erasureVersion,
        string outcome,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var eventId = FeedUserErasureEventIds.Create(requestId, erasureVersion, outcome);
        await context.PublishToBusOutboxAsync(
            publisher,
            new UserErasureCompleted
            {
                EventId = eventId,
                RequestId = requestId,
                UserId = userId,
                ErasureVersion = erasureVersion,
                Service = "feed",
                Outcome = outcome,
                CompletedAtUtc = new DateTimeOffset(nowUtc, TimeSpan.Zero),
            },
            eventId,
            cancellationToken);
    }

    private static void Validate(UserErasureRequested request, DateTime observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        var observed = NormalizeUtc(observedAtUtc);
        if (request.EventId == Guid.Empty
            || request.RequestId == Guid.Empty
            || request.UserId is <= 0 or > int.MaxValue
            || request.ErasureVersion <= 0
            || request.RequestedAtUtc < DateTimeOffset.UnixEpoch
            || request.RequestedAtUtc > new DateTimeOffset(observed, TimeSpan.Zero).AddMinutes(5))
            throw new ArgumentException("The Feed user erasure request is invalid.", nameof(request));
    }

    internal static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ when value == default => throw new ArgumentException("A timestamp is required."),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}

internal static class FeedUserErasureEventIds
{
    public static Guid Create(Guid requestId, long version, string outcome)
    {
        var input = Encoding.UTF8.GetBytes($"feed-user-erasure|{requestId:D}|{version}|{outcome}");
        var hash = SHA256.HashData(input);
        return new Guid(hash.AsSpan(0, 16));
    }
}
