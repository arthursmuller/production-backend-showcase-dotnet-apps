using Feed.Application.Features.Privacy;
using Feed.Application.Features.Feeds;
using Feed.Domain.AggregatesModel;
using Feed.Infra.Persistence;
using MessageBrokerContracts.Feed;
using Xunit;

namespace Feed.UnitTests;

public sealed class FeedReadDeadlineTests
{
    [Fact]
    public async Task Deadline_covers_the_initial_erasure_authority_call()
    {
        var authority = new DelayedErasureAuthority(delayInitial: true, delayFinal: false);
        var readCoreCalled = false;

        var exception = await Assert.ThrowsAsync<FeedReadBudgetExceededException>(() =>
            FeedReader.ExecuteReadPipelineAsync(
                viewerUserId: 7,
                readDeadline: TimeSpan.FromMilliseconds(100),
                authority,
                _ =>
                {
                    readCoreCalled = true;
                    return Task.FromResult(EmptyPage());
                },
                new FeedTelemetry(),
                TestContext.Current.CancellationToken));

        Assert.Equal(FeedReadBudgetReasons.ReadDeadline, exception.Reason);
        Assert.False(readCoreCalled);
        Assert.True(authority.InitialToken.CanBeCanceled);
    }

    [Fact]
    public async Task Deadline_covers_the_final_erasure_authority_call_after_the_core_read()
    {
        var authority = new DelayedErasureAuthority(delayInitial: false, delayFinal: true);
        CancellationToken coreToken = default;

        var exception = await Assert.ThrowsAsync<FeedReadBudgetExceededException>(() =>
            FeedReader.ExecuteReadPipelineAsync(
                viewerUserId: 7,
                readDeadline: TimeSpan.FromMilliseconds(100),
                authority,
                token =>
                {
                    coreToken = token;
                    return Task.FromResult(PageWithActor());
                },
                new FeedTelemetry(),
                TestContext.Current.CancellationToken));

        Assert.Equal(FeedReadBudgetReasons.ReadDeadline, exception.Reason);
        Assert.True(authority.FinalCalled);
        Assert.Equal(authority.InitialToken, coreToken);
        Assert.Equal(coreToken, authority.FinalToken);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reclassified_as_a_read_deadline()
    {
        var authority = new DelayedErasureAuthority(delayInitial: true, delayFinal: false);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        caller.CancelAfter(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FeedReader.ExecuteReadPipelineAsync(
                viewerUserId: 7,
                readDeadline: TimeSpan.FromSeconds(2),
                authority,
                _ => Task.FromResult(EmptyPage()),
                new FeedTelemetry(),
                caller.Token));

        Assert.True(caller.IsCancellationRequested);
    }

    private static FeedPage EmptyPage() => new([], null, false, false, 0);

    private static FeedPage PageWithActor() => new(
        [new FeedItem(
            EntryId: 1,
            ActivityId: 2,
            TargetType: "post",
            TargetId: "42",
            ActivityKind: "created",
            ActorUserId: 9,
            AudiencePolicy: FeedAudiencePolicies.Public,
            Summary: "summary",
            RankKey: 100,
            PublishedAtUtc: DateTime.UtcNow,
            SeenAtUtc: null,
            MaterializationMode: FeedDeliveryModes.Push)],
        null,
        false,
        false,
        1);

    private sealed class DelayedErasureAuthority(bool delayInitial, bool delayFinal)
        : IFeedUserErasureAuthority
    {
        public CancellationToken InitialToken { get; private set; }
        public CancellationToken FinalToken { get; private set; }
        public bool FinalCalled { get; private set; }

        public Task<IFeedUserMutationScope> AcquireMutationAsync(
            IReadOnlyCollection<int> userIds,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<bool> IsErasedAsync(
            int userId,
            CancellationToken cancellationToken)
        {
            InitialToken = cancellationToken;
            if (delayInitial)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return false;
        }

        public async Task<IReadOnlySet<int>> GetErasedAsync(
            IReadOnlyCollection<int> userIds,
            CancellationToken cancellationToken)
        {
            FinalCalled = true;
            FinalToken = cancellationToken;
            if (delayFinal)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HashSet<int>();
        }
    }
}
