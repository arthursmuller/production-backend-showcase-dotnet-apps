using Feed.Domain.AggregatesModel.Moderation;
using Xunit;

namespace Feed.UnitTests;

public sealed class FeedModerationDecisionDomainTests
{
    private static readonly Guid CaseId = Guid.Parse("72af86d6-45e8-426c-98d1-a6cc7463264e");
    private static readonly DateTime Occurred = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Gap_is_fail_closed_until_every_version_arrives_contiguously()
    {
        var versionTwo = Decision(2, FeedModerationActions.RemoveContent);
        var state = FeedModerationDecisionState.Create(versionTwo, Occurred.AddSeconds(2));

        Assert.Equal(0, state.LastAppliedVersion);
        Assert.Equal(2, state.HighestObservedVersion);
        Assert.Equal(FeedModerationProjectionStates.Gap, state.Status);
        Assert.True(state.IsSuppressed);

        Assert.Equal(
            FeedModerationVersionDisposition.Applied,
            state.Observe(Decision(1, FeedModerationActions.NoAction), Occurred.AddSeconds(3)));
        Assert.Equal(1, state.LastAppliedVersion);
        Assert.Equal(FeedModerationProjectionStates.Gap, state.Status);
        Assert.True(state.IsSuppressed);

        Assert.Equal(
            FeedModerationVersionDisposition.Applied,
            state.Observe(versionTwo, Occurred.AddSeconds(4)));
        Assert.Equal(2, state.LastAppliedVersion);
        Assert.Equal(FeedModerationProjectionStates.Healthy, state.Status);
        Assert.True(state.IsSuppressed);
        Assert.Equal(
            FeedModerationVersionDisposition.StaleOrDuplicate,
            state.Observe(versionTwo, Occurred.AddSeconds(5)));
    }

    [Fact]
    public void Healthy_next_no_action_releases_suppression_but_stale_decisions_cannot()
    {
        var state = FeedModerationDecisionState.Create(
            Decision(1, FeedModerationActions.RemoveContent),
            Occurred);
        Assert.True(state.IsSuppressed);

        Assert.Equal(
            FeedModerationVersionDisposition.Applied,
            state.Observe(Decision(2, FeedModerationActions.NoAction), Occurred.AddSeconds(1)));
        Assert.False(state.IsSuppressed);
        Assert.Equal(FeedModerationProjectionStates.Healthy, state.Status);

        Assert.Equal(
            FeedModerationVersionDisposition.StaleOrDuplicate,
            state.Observe(Decision(1, FeedModerationActions.RemoveContent), Occurred.AddSeconds(2)));
        Assert.False(state.IsSuppressed);
    }

    [Theory]
    [InlineData("unknown", "post_1", "no_action", 1)]
    [InlineData("post", "https://unsafe.example", "no_action", 1)]
    [InlineData("post", "post_1", "invented", 1)]
    [InlineData("post", "post_1", "no_action", 0)]
    public void Invalid_contract_values_are_rejected(
        string kind,
        string resourceId,
        string action,
        long version) =>
        Assert.ThrowsAny<ArgumentException>(() => FeedModerationDecisionInput.Create(
            CaseId,
            kind,
            resourceId,
            action,
            version,
            Occurred));

    private static FeedModerationDecisionInput Decision(long version, string action) =>
        FeedModerationDecisionInput.Create(
            CaseId,
            FeedModerationResourceKinds.Post,
            "post_123",
            action,
            version,
            Occurred.AddSeconds(version));
}
