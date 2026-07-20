using Interaction.Application.Dtos;
using Interaction.Application.Providers.Execution;
using Interaction.Application.Telemetry;

namespace Interaction.Application.Features.Maintenance.Queries;

public sealed record GetProjectionGapBacklogQuery(InteractionExecutionContext Context)
    : Query<ProjectionGapBacklogDto>
{
    public override async Task<ProjectionGapBacklogDto> Handle(
        CancellationToken cancellationToken)
    {
        var state = await Context.Repository.GetProjectionGapBacklogAsync(cancellationToken);
        var oldestAgeSeconds = state.OldestReceivedAtUtc is null
            ? 0
            : Math.Max(0, (Context.UtcNow() - state.OldestReceivedAtUtc.Value).TotalSeconds);
        var snapshot = new ProjectionGapBacklogDto(
            state.PendingCount,
            state.PendingCount == 0 ? 0 : oldestAgeSeconds);
        InteractionTelemetry.SetProjectionGapBacklog(snapshot);
        return snapshot;
    }
}
