using Comment.Application.Features.Privacy;
using Comment.Domain.AggregatesModel.Privacy;
using Comment.Domain.Exceptions;
using Xunit;

namespace Comment.UnitTests;

public sealed class CommentUserErasureDomainTests
{
    [Fact]
    public void Newer_version_supersedes_and_fences_the_old_worker()
    {
        var now = new DateTime(2026, 7, 17, 20, 0, 0, DateTimeKind.Utc);
        var erasure = CommentUserErasure.Schedule(
            42, Guid.NewGuid(), Guid.NewGuid(), 1, now, now);
        var oldToken = erasure.Claim("worker-old", now, TimeSpan.FromSeconds(30));
        Assert.True(erasure.OwnsLease("worker-old", oldToken, 1, now.AddSeconds(1)));

        erasure.Supersede(Guid.NewGuid(), Guid.NewGuid(), 2, now.AddSeconds(2), now.AddSeconds(2));

        Assert.False(erasure.OwnsLease("worker-old", oldToken, 1, now.AddSeconds(3)));
        Assert.Equal(CommentUserErasureStatus.Pending, erasure.Status);
        Assert.Equal(CommentUserErasureStage.MediaDiscovery, erasure.Stage);
        Assert.Equal(2, erasure.ErasureVersion);
    }

    [Fact]
    public void Completion_requires_the_fenced_final_stage_and_tombstone_remains_monotonic()
    {
        var now = new DateTime(2026, 7, 17, 20, 0, 0, DateTimeKind.Utc);
        var request = Guid.NewGuid();
        var erasure = CommentUserErasure.Schedule(
            73, request, Guid.NewGuid(), 4, now, now);
        var token = erasure.Claim("worker", now, TimeSpan.FromSeconds(30));
        Assert.Throws<CommentException>(() => erasure.Complete(
            Guid.NewGuid(), "worker", token, 4, now.AddSeconds(1)));

        erasure.Advance(CommentUserErasureStage.Complete, 0, "worker", token, 4,
            now.AddSeconds(1), TimeSpan.FromSeconds(30));
        var completion = Guid.NewGuid();
        erasure.Complete(completion, "worker", token, 4, now.AddSeconds(2));

        Assert.Equal(CommentUserErasureStatus.Completed, erasure.Status);
        Assert.Equal(request, erasure.RequestId);
        Assert.Equal(4, erasure.ErasureVersion);
        Assert.Equal(completion, erasure.CompletionEventId);
    }

    [Fact]
    public void Work_options_enforce_hard_row_time_and_lease_budgets()
    {
        Assert.True(CommentUserErasureOptions.IsValid(new CommentUserErasureOptions()));
        Assert.False(CommentUserErasureOptions.IsValid(new CommentUserErasureOptions
        {
            MaximumRowsPerCycle = 10,
            BatchSize = 50,
        }));
        Assert.False(CommentUserErasureOptions.IsValid(new CommentUserErasureOptions
        {
            CycleTimeBudgetMilliseconds = 10_000,
            LeaseSeconds = 10,
        }));
    }
}
