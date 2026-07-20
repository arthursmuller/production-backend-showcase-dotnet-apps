using Comment.Application.Dtos.Moderation;
using Comment.Application.Features.Moderation.Commands;
using Comment.Domain.AggregatesModel.Moderation;
using Comment.Domain.Repositories;
using Xunit;

namespace Comment.UnitTests;

public sealed class CommentModerationDecisionProjectionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 17, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Duplicate_stale_gap_and_next_versions_are_monotonic_and_fail_closed()
    {
        var projection = new CommentModerationDecisionProjection(
            Guid.NewGuid(), 42, Now);
        var first = Decision(CommentModerationAction.RemoveContent, 1, Now.AddMinutes(-3));

        Assert.Equal(CommentModerationObservation.Applied,
            projection.Observe(first, Now));
        Assert.True(projection.IsSuppressed);
        Assert.Equal(CommentModerationObservation.Ignored,
            projection.Observe(first, Now.AddSeconds(1)));

        var third = Decision(CommentModerationAction.NoAction, 3, Now.AddMinutes(-1));
        Assert.Equal(CommentModerationObservation.GapDetected,
            projection.Observe(third, Now.AddSeconds(2)));
        Assert.True(projection.HasGap);
        Assert.True(projection.IsSuppressed);
        Assert.Equal(1, projection.AppliedVersion);

        var second = Decision(CommentModerationAction.NoAction, 2, Now.AddMinutes(-2));
        Assert.Equal(CommentModerationObservation.Applied,
            projection.Observe(second, Now.AddSeconds(3)));
        Assert.True(projection.HasGap);
        Assert.True(projection.IsSuppressed);

        projection.ApplyBuffered(third, Now.AddSeconds(3));
        Assert.False(projection.HasGap);
        Assert.False(projection.IsSuppressed);
        Assert.Equal(3, projection.AppliedVersion);
        Assert.Equal(CommentModerationObservation.Ignored,
            projection.Observe(
                Decision(CommentModerationAction.RemoveContent, 2, Now),
                Now.AddSeconds(4)));
        Assert.False(projection.IsSuppressed);
    }

    [Fact]
    public async Task Wrong_resource_kind_is_ignored_without_repository_mutation()
    {
        var repository = new CapturingModerationRepository();
        var command = new ApplyCommentModerationDecisionCommand(
            repository,
            new FixedTimeProvider(Now));

        var result = await command.ExecuteAsync(
            new ApplyCommentModerationDecisionRequest(
                Guid.NewGuid(),
                CommentModeratedResourceKind.Post,
                "post-123",
                CommentModerationDecisionKind.RemoveContent,
                1,
                Now.AddMinutes(-1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(ApplyCommentModerationDecisionOutcome.NotApplicable, result.Outcome);
        Assert.Equal(0, repository.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData("2147483648")]
    [InlineData(" 42")]
    public async Task Invalid_comment_resource_ids_are_rejected(string resourceId)
    {
        var command = new ApplyCommentModerationDecisionCommand(
            new CapturingModerationRepository(),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync(
            new ApplyCommentModerationDecisionRequest(
                Guid.NewGuid(),
                CommentModeratedResourceKind.Comment,
                resourceId,
                CommentModerationDecisionKind.NoAction,
                1,
                Now),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Invalid_time_and_unknown_action_are_rejected()
    {
        var command = new ApplyCommentModerationDecisionCommand(
            new CapturingModerationRepository(),
            new FixedTimeProvider(Now));
        var valid = new ApplyCommentModerationDecisionRequest(
            Guid.NewGuid(),
            CommentModeratedResourceKind.Comment,
            "42",
            CommentModerationDecisionKind.NoAction,
            1,
            Now);

        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync(
            valid with { OccurredAtUtc = Now.AddMinutes(6) },
            TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync(
            valid with { Action = (CommentModerationDecisionKind)999 },
            TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync(
            valid with { OccurredAtUtc = Now.ToOffset(TimeSpan.FromHours(1)) },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Application_contract_cannot_carry_case_evidence_or_rationale()
    {
        var propertyNames = typeof(ApplyCommentModerationDecisionRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(
            ["CaseId", "ResourceKind", "ResourceId", "Action", "DecisionVersion", "OccurredAtUtc"],
            propertyNames);
    }

    private static CommentModerationDecisionSnapshot Decision(
        CommentModerationAction action,
        long version,
        DateTimeOffset occurredAtUtc) => new(action, version, occurredAtUtc);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CapturingModerationRepository
        : ICommentModerationDecisionRepository
    {
        public int CallCount { get; private set; }

        public Task<CommentModerationApplyResult> ApplyAsync(
            Guid caseId,
            int commentId,
            CommentModerationDecisionSnapshot decision,
            DateTimeOffset observedAtUtc,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new CommentModerationApplyResult(
                CommentModerationApplyOutcome.Applied,
                decision.DecisionVersion,
                decision.DecisionVersion,
                decision.Action != CommentModerationAction.NoAction,
                0));
        }
    }
}
