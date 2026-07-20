using Feed.Application.Features.Feeds;
using Feed.Domain.AggregatesModel;
using Feed.Domain.AggregatesModel.Moderation;
using Feed.Domain.Repositories;
using MessageBrokerContracts.Feed;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Infra.Persistence.Moderation;

public sealed class FeedModerationDecisionRepository(
    FeedContext context,
    TimeProvider timeProvider) : IFeedModerationDecisionRepository
{
    private const int MaximumGapDrain = 100;
    private const int MaximumActivitiesPerDecision = 256;

    public async Task ApplyAsync(
        FeedModerationDecisionInput decision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = FeedModerationDecisionInput.Create(
            decision.CaseId,
            decision.ResourceKind,
            decision.ResourceId,
            decision.Action,
            decision.DecisionVersion,
            decision.OccurredAtUtc);
        var observed = timeProvider.GetUtcNow().UtcDateTime;
        var state = await context.FeedModerationDecisionStates.SingleOrDefaultAsync(
            item => item.CaseId == normalized.CaseId
                && item.ResourceKind == normalized.ResourceKind
                && item.ResourceId == normalized.ResourceId,
            cancellationToken);
        var wasSuppressed = state?.IsSuppressed ?? false;
        FeedModerationVersionDisposition disposition;
        if (state is null)
        {
            state = FeedModerationDecisionState.Create(normalized, observed);
            await context.FeedModerationDecisionStates.AddAsync(state, cancellationToken);
            disposition = normalized.DecisionVersion == 1
                ? FeedModerationVersionDisposition.Applied
                : FeedModerationVersionDisposition.Gap;
        }
        else
        {
            disposition = state.Observe(normalized, observed);
            if (disposition == FeedModerationVersionDisposition.StaleOrDuplicate)
                return;
        }

        if (disposition == FeedModerationVersionDisposition.Gap)
        {
            await EnsureGapAsync(normalized, observed, cancellationToken);
        }
        else
        {
            await RemoveGapAsync(normalized, cancellationToken);
            await DrainContiguousGapsAsync(state, observed, cancellationToken);
        }

        var activities = await context.FeedActivities
            .Where(activity => activity.TargetType == state.ResourceKind
                && activity.TargetId == state.ResourceId)
            .OrderBy(activity => activity.Id)
            .Take(MaximumActivitiesPerDecision + 1)
            .ToListAsync(cancellationToken);
        if (activities.Count > MaximumActivitiesPerDecision)
        {
            state.RequireReconciliation(observed);
            activities.RemoveAt(activities.Count - 1);
        }

        if (!wasSuppressed && state.IsSuppressed)
        {
            await ScheduleRevocationAsync(
                state,
                activities,
                observed,
                cancellationToken);
        }
        else if (wasSuppressed && !state.IsSuppressed)
        {
            await ScheduleReleaseAsync(
                state,
                activities,
                observed,
                cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureGapAsync(
        FeedModerationDecisionInput decision,
        DateTime observedAtUtc,
        CancellationToken cancellationToken)
    {
        var tracked = context.FeedModerationDecisionGaps.Local.Any(gap =>
            gap.CaseId == decision.CaseId
            && gap.ResourceKind == decision.ResourceKind
            && gap.ResourceId == decision.ResourceId
            && gap.DecisionVersion == decision.DecisionVersion);
        if (tracked || await context.FeedModerationDecisionGaps.AnyAsync(gap =>
                gap.CaseId == decision.CaseId
                && gap.ResourceKind == decision.ResourceKind
                && gap.ResourceId == decision.ResourceId
                && gap.DecisionVersion == decision.DecisionVersion,
                cancellationToken))
        {
            return;
        }

        await context.FeedModerationDecisionGaps.AddAsync(
            FeedModerationDecisionGap.Create(decision, observedAtUtc),
            cancellationToken);
    }

    private async Task RemoveGapAsync(
        FeedModerationDecisionInput decision,
        CancellationToken cancellationToken)
    {
        var gap = context.FeedModerationDecisionGaps.Local.FirstOrDefault(item =>
                item.CaseId == decision.CaseId
                && item.ResourceKind == decision.ResourceKind
                && item.ResourceId == decision.ResourceId
                && item.DecisionVersion == decision.DecisionVersion)
            ?? await context.FeedModerationDecisionGaps.SingleOrDefaultAsync(item =>
                item.CaseId == decision.CaseId
                && item.ResourceKind == decision.ResourceKind
                && item.ResourceId == decision.ResourceId
                && item.DecisionVersion == decision.DecisionVersion,
                cancellationToken);
        if (gap is not null)
            context.FeedModerationDecisionGaps.Remove(gap);
    }

    private async Task DrainContiguousGapsAsync(
        FeedModerationDecisionState state,
        DateTime observedAtUtc,
        CancellationToken cancellationToken)
    {
        var pending = await context.FeedModerationDecisionGaps
            .Where(gap => gap.CaseId == state.CaseId
                && gap.ResourceKind == state.ResourceKind
                && gap.ResourceId == state.ResourceId
                && gap.DecisionVersion > state.LastAppliedVersion)
            .OrderBy(gap => gap.DecisionVersion)
            .Take(MaximumGapDrain + 1)
            .ToListAsync(cancellationToken);
        var drained = 0;
        foreach (var gap in pending.Take(MaximumGapDrain))
        {
            if (gap.DecisionVersion != state.LastAppliedVersion + 1)
                break;
            state.Observe(gap.ToInput(), observedAtUtc);
            context.FeedModerationDecisionGaps.Remove(gap);
            drained++;
        }
        if (pending.Count > MaximumGapDrain
            || (pending.Count > drained
                && pending[drained].DecisionVersion != state.LastAppliedVersion + 1))
        {
            state.RequireReconciliation(observedAtUtc);
        }
        else if (state.NeedsReconciliation
            && state.Status == FeedModerationProjectionStates.Healthy)
        {
            state.CompleteReconciliation(observedAtUtc);
        }
    }

    private async Task ScheduleRevocationAsync(
        FeedModerationDecisionState state,
        System.Collections.Generic.IReadOnlyCollection<FeedActivity> activities,
        DateTime observedAtUtc,
        CancellationToken cancellationToken)
    {
        var occurred = state.LastSourceOccurredAtUtc ?? observedAtUtc;
        foreach (var activity in activities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = SourceId(state, "revoke", activity.Id);
            if (context.FeedRevocationWorkItems.Local.Any(work => work.SourceEventId == source)
                || await context.FeedRevocationWorkItems.AnyAsync(
                    work => work.SourceEventId == source,
                    cancellationToken))
            {
                continue;
            }
            await context.FeedRevocationWorkItems.AddAsync(
                FeedRevocationWorkItem.ForActivity(
                    source,
                    activity,
                    occurred,
                    observedAtUtc),
                cancellationToken);
        }
    }

    private async Task ScheduleReleaseAsync(
        FeedModerationDecisionState state,
        System.Collections.Generic.IReadOnlyCollection<FeedActivity> activities,
        DateTime observedAtUtc,
        CancellationToken cancellationToken)
    {
        foreach (var activity in activities.Where(activity =>
            !activity.IsTombstoned
            && activity.DeliveryMode == FeedDeliveryModes.Push
            && activity.AudiencePolicy != FeedAudiencePolicies.Owner))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = SourceId(state, "release", activity.Id);
            if (context.FanoutWorkItems.Local.Any(work => work.SourceEventId == source)
                || await context.FanoutWorkItems.AnyAsync(
                    work => work.SourceEventId == source,
                    cancellationToken))
            {
                continue;
            }
            await context.FanoutWorkItems.AddAsync(
                FanoutWorkItem.Create(source, activity, observedAtUtc),
                cancellationToken);
        }
    }

    private static string SourceId(
        FeedModerationDecisionState state,
        string operation,
        long activityId) =>
        $"moderation:{state.CaseId:N}:{state.LastAppliedVersion}:{operation}:{activityId}";
}
