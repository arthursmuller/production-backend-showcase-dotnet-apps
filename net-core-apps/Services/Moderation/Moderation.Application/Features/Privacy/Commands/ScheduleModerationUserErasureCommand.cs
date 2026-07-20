using System.Security.Cryptography;
using System.Text;
using MessageBrokerContracts.Privacy.Restricted;
using Moderation.Application.Providers;
using Moderation.Application.Telemetry;
using Moderation.Domain.AggregatesModel;
using Moderation.Domain.Exceptions;
using Moderation.Domain.Repositories;

namespace Moderation.Application.Features.Privacy.Commands;

public sealed class ScheduleModerationUserErasureCommand(
    IModerationUserErasureStore store,
    IModerationEventPublisher publisher,
    TimeProvider timeProvider)
{
    public async Task<bool> Handle(
        UserErasureRequested request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        if (request.EventId == Guid.Empty
            || request.RequestId == Guid.Empty
            || request.UserId <= 0
            || request.ErasureVersion <= 0
            || request.RequestedAtUtc < DateTimeOffset.UnixEpoch
            || request.RequestedAtUtc > now.AddMinutes(5))
        {
            throw new ModerationValidationException(
                "The user-erasure request is invalid.");
        }

        await using var transaction =
            await store.BeginSerializableAsync(cancellationToken);
        var current = await store.GetForUpdateAsync(
            request.UserId,
            cancellationToken);
        var outcome = "scheduled";
        if (current is null)
        {
            store.Add(ModerationUserErasure.Schedule(
                request.UserId,
                request.RequestId,
                request.ErasureVersion,
                request.RequestedAtUtc,
                now));
        }
        else if (request.ErasureVersion < current.ErasureVersion)
        {
            outcome = "stale";
            await PublishCompletionAsync(
                request.RequestId,
                request.UserId,
                request.ErasureVersion,
                UserErasureOutcomes.Superseded,
                now,
                cancellationToken);
        }
        else if (request.ErasureVersion == current.ErasureVersion)
        {
            if (request.RequestId != current.RequestId)
                throw new ModerationConflictException(
                    "An erasure version cannot identify two requests.");
            await transaction.CommitAsync(cancellationToken);
            ModerationTelemetry.ErasureRequests.Add(
                1,
                new KeyValuePair<string, object?>("outcome", "idempotent"));
            return false;
        }
        else
        {
            outcome = "superseding";
            if (current.CompletedAtUtc is null)
            {
                await PublishCompletionAsync(
                    current.RequestId,
                    current.UserId,
                    current.ErasureVersion,
                    UserErasureOutcomes.Superseded,
                    now,
                    cancellationToken);
            }
            current.Supersede(
                request.RequestId,
                request.ErasureVersion,
                request.RequestedAtUtc,
                now);
        }

        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        ModerationTelemetry.ErasureRequests.Add(
            1,
            new KeyValuePair<string, object?>("outcome", outcome));
        return true;
    }

    private Task PublishCompletionAsync(
        Guid requestId,
        long userId,
        long erasureVersion,
        string outcome,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        publisher.PublishErasureCompletedAsync(
            new UserErasureCompleted
            {
                EventId = ModerationErasureEventIds.Create(
                    requestId,
                    erasureVersion,
                    outcome),
                RequestId = requestId,
                UserId = userId,
                ErasureVersion = erasureVersion,
                Service = "moderation",
                Outcome = outcome,
                CompletedAtUtc = now,
            },
            cancellationToken);
}

internal static class ModerationErasureEventIds
{
    internal static Guid Create(
        Guid requestId,
        long erasureVersion,
        string outcome)
    {
        var material = Encoding.UTF8.GetBytes(
            $"moderation-erasure:{requestId:D}:{erasureVersion}:{outcome}");
        var bytes = SHA256.HashData(material)[..16];
        bytes[7] = (byte)((bytes[7] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }
}
