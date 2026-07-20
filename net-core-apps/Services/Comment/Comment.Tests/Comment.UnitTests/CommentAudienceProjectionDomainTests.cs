using Comment.Domain.AggregatesModel.Visibility;
using Xunit;

namespace Comment.UnitTests;

public sealed class CommentAudienceProjectionDomainTests
{
    [Fact]
    public void Checkpoint_detects_gap_and_only_authoritative_reconciliation_restores_health()
    {
        var now = DateTime.UtcNow;
        var stream = Guid.NewGuid();
        var checkpoint = CommentRelationshipCheckpoint.Create(stream, 11, 22, "friend", now);
        checkpoint.Apply(Guid.NewGuid(), 1, now, now);

        Assert.Equal(CommentProjectionVersionDisposition.Gap,
            checkpoint.Classify(stream, 11, 22, "friend", 3));
        checkpoint.Gap(3, now.AddSeconds(1));
        Assert.Equal(CommentProjectionStates.Gap, checkpoint.Status);
        Assert.Equal(2, checkpoint.GapFromVersion);
        Assert.Equal(2, checkpoint.GapToVersion);

        checkpoint.Reconcile(Guid.NewGuid(), 3, now.AddSeconds(2), now.AddSeconds(2));
        Assert.Equal(CommentProjectionStates.Healthy, checkpoint.Status);
        Assert.Equal(3, checkpoint.LastAppliedVersion);
        Assert.Null(checkpoint.GapFromVersion);
    }

    [Fact]
    public void Bootstrap_rejects_a_moving_high_watermark_and_is_not_fresh_before_completion()
    {
        var now = DateTime.UtcNow;
        var state = CommentAudienceProjectionState.Create(now);
        Assert.False(state.IsCompleteAndFresh(now, TimeSpan.FromHours(1)));

        var firstLease = state.TryLease("worker-a", now, TimeSpan.FromMinutes(1));
        state.BeginCycle(firstLease!.Value, now);
        state.ApplyPage(firstLease.Value, 10, now, 5, true, now.AddSeconds(1));

        var secondLease = state.TryLease("worker-b", now.AddSeconds(2), TimeSpan.FromMinutes(1));
        Assert.Throws<InvalidOperationException>(() => state.ApplyPage(
            secondLease!.Value, 11, now, 10, false, now.AddSeconds(3)));
    }

    [Fact]
    public void Fail_closed_edge_revokes_friend_but_preserves_block_denial()
    {
        var now = DateTime.UtcNow;
        var friend = CommentRelationshipEdge.Create(11, 22, "friend", "active",
            Guid.NewGuid(), Guid.NewGuid(), 1, now, now);
        var friendEventId = friend.LastEventId;
        friend.FailClosed("revoked", now.AddSeconds(1));
        Assert.Equal("revoked", friend.State);
        Assert.Equal(1, friend.SourceVersion);
        Assert.Equal(friendEventId, friend.LastEventId);

        var block = CommentRelationshipEdge.Create(11, 22, "block", "blocked",
            Guid.NewGuid(), Guid.NewGuid(), 1, now, now);
        var blockEventId = block.LastEventId;
        block.FailClosed("blocked", now.AddSeconds(1));
        Assert.Equal("blocked", block.State);
        Assert.Equal(1, block.SourceVersion);
        Assert.Equal(blockEventId, block.LastEventId);
    }
}
