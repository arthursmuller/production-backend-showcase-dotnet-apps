namespace Interaction.Application.Dtos;

public sealed record ProjectionGapBacklogDto(
    long PendingCount,
    double OldestAgeSeconds);
