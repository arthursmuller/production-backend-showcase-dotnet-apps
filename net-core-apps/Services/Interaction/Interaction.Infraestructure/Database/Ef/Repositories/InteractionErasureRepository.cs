using Interaction.Domain.AggregatesModel;
using Interaction.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Interaction.Infraestructure.Database.Ef.Repositories;

public sealed class InteractionErasureRepository(InteractionDbContext context)
    : IInteractionErasureRepository
{
    public Task<InteractionUserErasure?> GetForUpdateAsync(
        long userId,
        CancellationToken cancellationToken) =>
        context.UserErasures
            .FromSqlInterpolated($"SELECT * FROM `InteractionUserErasures` WHERE `UserId` = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public void Add(InteractionUserErasure erasure) => context.UserErasures.Add(erasure);

    public Task<InteractionUserErasure?> LockNextPendingAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        context.UserErasures.FromSqlInterpolated($"""
            SELECT *
            FROM `InteractionUserErasures`
            WHERE (
                    `Stage` = {(byte)InteractionUserErasureStage.ActorData}
                    OR (
                        `Stage` = {(byte)InteractionUserErasureStage.AwaitingRawViewExpiry}
                        AND `RawViewsExpireAtUtc` <= {now}
                    )
                  )
              AND (`LeaseUntilUtc` IS NULL OR `LeaseUntilUtc` <= {now})
            ORDER BY COALESCE(`LastProcessedAtUtc`, `RequestedAtUtc`),
                     `RequestedAtUtc`,
                     `UserId`
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Reaction>> LockReactionsAsync(
        long userId,
        int maximum,
        CancellationToken cancellationToken) =>
        await context.Reactions.FromSqlInterpolated($"""
            SELECT *
            FROM `Reactions`
            WHERE `ActorId` = {userId}
            ORDER BY `ResourceType`, `ResourceId`
            LIMIT {maximum}
            FOR UPDATE SKIP LOCKED
            """).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Rating>> LockRatingsAsync(
        long userId,
        int maximum,
        CancellationToken cancellationToken) =>
        await context.Ratings.FromSqlInterpolated($"""
            SELECT *
            FROM `Ratings`
            WHERE `ActorId` = {userId}
            ORDER BY `ResourceType`, `ResourceId`
            LIMIT {maximum}
            FOR UPDATE SKIP LOCKED
            """).ToArrayAsync(cancellationToken);

    public async Task<bool> HasActorDataAsync(
        long userId,
        CancellationToken cancellationToken) =>
        await context.Reactions.AsNoTracking().AnyAsync(item => item.ActorId == userId, cancellationToken)
        || await context.Ratings.AsNoTracking().AnyAsync(item => item.ActorId == userId, cancellationToken);
}
