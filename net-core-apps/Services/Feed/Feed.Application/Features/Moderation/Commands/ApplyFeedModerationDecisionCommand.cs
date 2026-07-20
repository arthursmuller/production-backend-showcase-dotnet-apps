using Feed.Application.Dtos.Moderation;
using Feed.Domain.AggregatesModel.Moderation;
using Feed.Domain.Repositories;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Application.Features.Moderation;

public sealed class ApplyFeedModerationDecisionCommand(
    IFeedModerationDecisionRepository repository)
{
    public Task HandleAsync(
        FeedModerationDecisionDto decision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var input = FeedModerationDecisionInput.Create(
            decision.CaseId,
            decision.ResourceKind,
            decision.ResourceId,
            decision.Action,
            decision.DecisionVersion,
            decision.OccurredAtUtc);
        return repository.ApplyAsync(input, cancellationToken);
    }
}
