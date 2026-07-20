using Comment.Application.Features.Privacy;
using Comment.Domain.AggregatesModel.Privacy;
using Comment.Domain.Exceptions;
using MessageBrokerContracts.Privacy.Restricted;
using Microsoft.EntityFrameworkCore;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Persistence.Privacy;

public sealed class CommentUserErasureScheduler(
    CommentContext context,
    CommentUserErasureAuthority authority,
    ICommentPrivacyEventPublisher events,
    CommentUserErasureTelemetry telemetry) : ICommentUserErasureScheduler
{
    public async Task<CommentUserErasureScheduleResult> ScheduleAsync(
        UserErasureRequested request,
        DateTime observedAtUtc,
        CancellationToken cancellationToken)
    {
        var userId = checked((int)request.UserId);
        var now = Utc(observedAtUtc);
        await using var mutation = await authority.AcquireErasureMutationAsync(
            userId, cancellationToken);
        var current = await context.CommentUserErasures
            .SingleOrDefaultAsync(value => value.UserId == userId, cancellationToken);

        if (current is null)
        {
            context.CommentUserErasures.Add(CommentUserErasure.Schedule(
                userId,
                request.RequestId,
                request.EventId,
                request.ErasureVersion,
                request.RequestedAtUtc.UtcDateTime,
                now));
            await context.SaveChangesAsync(cancellationToken);
            await mutation.CommitAsync(cancellationToken);
            telemetry.Schedule("scheduled");
            return new(true, false, false, request.ErasureVersion);
        }

        if (request.ErasureVersion < current.ErasureVersion)
        {
            await StageAcknowledgementAsync(
                request.RequestId,
                userId,
                request.ErasureVersion,
                UserErasureOutcomes.Superseded,
                now,
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await mutation.CommitAsync(cancellationToken);
            telemetry.Schedule("superseded");
            return new(false, false, true, current.ErasureVersion);
        }

        if (request.ErasureVersion == current.ErasureVersion)
        {
            if (request.RequestId != current.RequestId)
                throw new CommentException("An erasure version cannot identify two requests.", 409);
            await mutation.CommitAsync(cancellationToken);
            telemetry.Schedule("duplicate");
            return new(false, true, false, current.ErasureVersion);
        }

        if (current.Status != CommentUserErasureStatus.Completed)
        {
            await StageAcknowledgementAsync(
                current.RequestId,
                current.UserId,
                current.ErasureVersion,
                UserErasureOutcomes.Superseded,
                now,
                cancellationToken);
        }
        current.Supersede(
            request.RequestId,
            request.EventId,
            request.ErasureVersion,
            request.RequestedAtUtc.UtcDateTime,
            now);
        await context.SaveChangesAsync(cancellationToken);
        await mutation.CommitAsync(cancellationToken);
        telemetry.Schedule("scheduled");
        return new(true, false, false, request.ErasureVersion);
    }

    private Task StageAcknowledgementAsync(
        Guid requestId,
        int userId,
        long erasureVersion,
        string outcome,
        DateTime completedAtUtc,
        CancellationToken cancellationToken) => events.StageCompletionAsync(
        new UserErasureCompleted
        {
            EventId = CommentUserErasureEventIds.Create(requestId, erasureVersion, outcome),
            RequestId = requestId,
            UserId = userId,
            ErasureVersion = erasureVersion,
            Service = "comment",
            Outcome = outcome,
            CompletedAtUtc = new DateTimeOffset(completedAtUtc, TimeSpan.Zero),
        }, cancellationToken);

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}

internal static class CommentUserErasureEventIds
{
    public static Guid Create(Guid requestId, long version, string outcome)
    {
        var material = Encoding.UTF8.GetBytes(
            $"comment-user-erasure|{requestId:D}|{version}|{outcome}");
        return new Guid(SHA256.HashData(material).AsSpan(0, 16));
    }
}
