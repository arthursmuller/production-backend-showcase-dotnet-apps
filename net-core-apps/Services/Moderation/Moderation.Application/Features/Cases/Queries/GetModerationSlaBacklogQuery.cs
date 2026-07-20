using Moderation.Application.Dtos;
using Moderation.Application.Telemetry;
using Moderation.Domain.Repositories;

namespace Moderation.Application.Features.Cases.Queries;

public sealed class GetModerationSlaBacklogQuery(
    IModerationRepository repository,
    TimeProvider timeProvider)
{
    public async Task<ModerationSlaBacklogDto> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var state = await repository.GetSlaBacklogAsync(now, cancellationToken);
        var oldestAge = state.OldestOverdueDueAtUtc is null
            ? 0
            : Math.Max(0, (now - state.OldestOverdueDueAtUtc.Value).TotalSeconds);
        var snapshot = new ModerationSlaBacklogDto(
            state.ActiveCount,
            state.OverdueCount,
            state.OverdueCount == 0 ? 0 : oldestAge);
        ModerationTelemetry.SetSlaBacklog(snapshot);
        return snapshot;
    }
}
