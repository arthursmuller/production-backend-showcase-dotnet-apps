using Interaction.Domain.AggregatesModel;
using Interaction.Domain.Exceptions;
using Xunit;

namespace Interaction.UnitTests;

public sealed class InteractionDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("comment", ResourceType.Comment)]
    [InlineData("POST", ResourceType.Post)]
    public void Resource_reference_is_typed_and_allow_listed(string input, ResourceType expected)
    {
        var resource = ResourceRef.Parse(input, "opaque-001");
        Assert.Equal(expected, resource.Type);
        Assert.Equal("opaque-001", resource.Id);
        Assert.Throws<InteractionValidationException>(() => ResourceRef.Parse("report", "1"));
    }

    [Fact]
    public void Projection_gap_fails_closed_until_every_version_is_contiguous()
    {
        var state = new ResourceState(new ResourceRef(ResourceType.Comment, "42"), Now);
        state.ApplyNext(ResourceLifecycle.Active, ResourceVisibility.Public, 1, Now);
        Assert.True(state.IsPubliclyInteractable);

        Assert.True(state.ObserveGap(3, Now.AddSeconds(1)));
        Assert.True(state.HasGap);
        Assert.False(state.IsPubliclyInteractable);

        state.ApplyNext(ResourceLifecycle.Active, ResourceVisibility.Public, 2, Now.AddSeconds(2));
        Assert.False(state.IsPubliclyInteractable);
        state.ApplyNext(ResourceLifecycle.Active, ResourceVisibility.Public, 3, Now.AddSeconds(3));
        Assert.True(state.IsPubliclyInteractable);
    }

    [Fact]
    public void Like_to_dislike_switch_is_mutually_exclusive_and_nonnegative()
    {
        var resource = new ResourceRef(ResourceType.Post, "p1");
        var reaction = new Reaction(resource, 9, ReactionKind.Like, Now);
        var summary = new EngagementSummary(resource, Now);

        summary.ApplyReaction(null, reaction.Kind, Now);
        Assert.Equal(1, summary.LikeCount);
        Assert.Equal(0, summary.DislikeCount);

        var previous = reaction.Kind;
        Assert.True(reaction.SwitchTo(ReactionKind.Dislike, Now.AddSeconds(1)));
        summary.ApplyReaction(previous, reaction.Kind, Now.AddSeconds(1));
        Assert.Equal(0, summary.LikeCount);
        Assert.Equal(1, summary.DislikeCount);
    }

    [Fact]
    public void Rating_enforces_bounded_scale_and_comment()
    {
        var resource = new ResourceRef(ResourceType.Comment, "c1");
        Assert.Throws<InteractionValidationException>(() => new Rating(resource, 1, 0, null, Now));
        Assert.Throws<InteractionValidationException>(() => new Rating(resource, 1, 7, null, Now));
        Assert.Throws<InteractionValidationException>(() => new Rating(resource, 1, 5, new string('x', 2_001), Now));
        var rating = new Rating(resource, 1, 5, "good", Now);
        Assert.Equal((byte)5, rating.Score);
    }

    [Fact]
    public void Known_contribution_gap_keeps_public_contributions_fail_closed()
    {
        var summary = new EngagementSummary(new ResourceRef(ResourceType.Comment, "c-gap"), Now);
        Assert.False(summary.ApplyContribution(30, 4, 3, Now));
        Assert.True(summary.ContributionHasGap);
        Assert.True(summary.ApplyContribution(10, 1, 1, Now.AddSeconds(1)));
        Assert.True(summary.ContributionHasGap);
        Assert.True(summary.ApplyContribution(20, 2, 2, Now.AddSeconds(2)));
        Assert.True(summary.ContributionHasGap);
        Assert.True(summary.ApplyContribution(30, 4, 3, Now.AddSeconds(3)));
        Assert.False(summary.ContributionHasGap);
    }

    [Fact]
    public void Public_contract_has_no_report_or_moderation_fields()
    {
        var names = typeof(Interaction.Contracts.EngagementSummaryChanged)
            .GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(names, name => name.Contains("Report", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Moderation", StringComparison.OrdinalIgnoreCase));
    }
}
