using Feed.API.AppInterfaces.Queues;
using Feed.Application.Features.Feeds;
using MassTransit;
using MassTransit.Testing;
using MessageBrokerContracts.Feed;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MassTransit.HarnessTests;

public sealed class FeedConsumerHarnessTests
{
    [Fact]
    public async Task Canonical_feed_activity_is_consumed_and_projected_once()
    {
        var projection = new RecordingFeedActivityProjection();
        await using var provider = new ServiceCollection()
            .AddSingleton<IFeedActivityProjectionWriter>(projection)
            .AddSingleton(TimeProvider.System)
            .AddMassTransitTestHarness(registration =>
            {
                registration.SetTestTimeouts(
                    testTimeout: TimeSpan.FromSeconds(15),
                    testInactivityTimeout: TimeSpan.FromSeconds(2));
                registration.AddConsumer<FeedActivityChangedConsumer>();
            })
            .BuildServiceProvider(validateScopes: true);

        var harness = await provider.StartTestHarness();
        var message = new FeedActivityChanged
        {
            EventId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            StreamId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            StreamVersion = 1,
            TargetType = "post",
            TargetId = "42",
            ActivityKind = "created",
            ActorUserId = 7,
            AudiencePolicy = FeedAudiencePolicies.Public,
            State = FeedActivityStates.Active,
            RankKey = 1,
            PublishedAtUtc = new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc),
            OccurredAtUtc = new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc),
            ReasonCode = "post.created",
        };

        await harness.Bus.Publish(
            message,
            context => context.MessageId = message.EventId,
            TestContext.Current.CancellationToken);

        Assert.True(await harness.Consumed.Any<FeedActivityChanged>(
            TestContext.Current.CancellationToken));
        var consumer = harness.GetConsumerHarness<FeedActivityChangedConsumer>();
        Assert.True(await consumer.Consumed.Any<FeedActivityChanged>(
            TestContext.Current.CancellationToken));
        Assert.Equal([message.EventId], projection.ProjectedEventIds);
    }

    private sealed class RecordingFeedActivityProjection : IFeedActivityProjectionWriter
    {
        public List<Guid> ProjectedEventIds { get; } = [];

        public Task ApplyCanonicalAsync(
            FeedActivityChanged message,
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProjectedEventIds.Add(message.EventId);
            return Task.CompletedTask;
        }
    }
}
