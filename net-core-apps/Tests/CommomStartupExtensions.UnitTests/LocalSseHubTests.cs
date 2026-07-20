using System.Text.Json;
using CommomStartupExtensions.Sse;
using Xunit;

namespace CommomStartupExtensions.UnitTests;

public sealed class LocalSseHubTests
{
    [Fact]
    public async Task Publish_fans_out_only_to_the_matching_typed_key_and_serializes_once()
    {
        using var hub = new LocalSseHub();
        using var first = hub.Subscribe("presence::updates", 42, CancellationToken.None);
        using var second = hub.Subscribe("presence::updates", 42, CancellationToken.None);
        using var otherChannel = hub.Subscribe("other", 42, CancellationToken.None);
        using var otherUser = hub.Subscribe("presence::updates", 43, CancellationToken.None);

        await hub.Publish("presence::updates", 42, new Payload
        {
            Id = 7,
            Label = "<safe>",
            PublicField = 11
        }, TestContext.Current.CancellationToken);
        await hub.Publish(" presence::updates ", 42, "raw\nstring\"payload", TestContext.Current.CancellationToken);
        await hub.Publish("other", 42, "other-only", TestContext.Current.CancellationToken);
        await hub.Publish("presence::updates", 43, "other-user-only", TestContext.Current.CancellationToken);

        var firstMessages = await ReadExactly(first, 2);
        var secondMessages = await ReadExactly(second, 2);
        var firstPayload = firstMessages[0];
        var secondPayload = secondMessages[0];
        Assert.Equal(firstPayload, secondPayload);

        using var json = JsonDocument.Parse(firstPayload);
        Assert.Equal(7, json.RootElement.GetProperty(nameof(Payload.Id)).GetInt32());
        Assert.Equal("<safe>", json.RootElement.GetProperty(nameof(Payload.Label)).GetString());
        Assert.Equal(11, json.RootElement.GetProperty(nameof(Payload.PublicField)).GetInt32());
        Assert.Equal("raw\nstring\"payload", firstMessages[1]);
        Assert.Equal(firstMessages[1], secondMessages[1]);
        Assert.Equal("other-only", await ReadOne(otherChannel));
        Assert.Equal("other-user-only", await ReadOne(otherUser));
    }

    [Fact]
    public async Task Slow_subscriber_drops_oldest_events_and_keeps_the_bounded_window()
    {
        using var hub = new LocalSseHub();
        using var subscription = hub.Subscribe("feed", 12, CancellationToken.None);
        const int overflow = 5;

        for (var index = 0; index < LocalSseHub.SubscriberBufferCapacity + overflow; index++)
            await hub.Publish("feed", 12, $"event-{index}", TestContext.Current.CancellationToken);

        var received = await ReadExactly(subscription, LocalSseHub.SubscriberBufferCapacity);

        Assert.Equal($"event-{overflow}", received[0]);
        Assert.Equal(
            $"event-{LocalSseHub.SubscriberBufferCapacity + overflow - 1}",
            received[^1]);
    }

    [Fact]
    public async Task Inputs_are_validated_and_unsupported_payloads_fail_safely()
    {
        using var hub = new LocalSseHub();

        Assert.Throws<ArgumentNullException>(() => hub.Subscribe(null!, 1, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => hub.Subscribe(string.Empty, 1, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => hub.Subscribe("   ", 1, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => hub.Subscribe("bad\nchannel", 1, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            hub.Subscribe(new string('a', LocalSseHub.MaximumChannelLength + 1), 1, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            hub.Subscribe("valid", 0, CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            hub.Publish("valid", 1, null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            hub.Publish("valid", -1, "data", TestContext.Current.CancellationToken));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            hub.Publish("valid", 1, (Action)(() => { }), TestContext.Current.CancellationToken));
        Assert.IsType<NotSupportedException>(exception.InnerException);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            hub.Publish("valid", 1, "data", cancelled.Token));
    }

    [Fact]
    public void Snapshot_and_disposal_track_only_live_local_subscribers()
    {
        using var hub = new LocalSseHub();
        var first = hub.Subscribe("notifications", 9, CancellationToken.None);
        var second = hub.Subscribe("notifications", 9, CancellationToken.None);

        var snapshot = Assert.Single(hub.SnapshotLocalSubscribers());
        Assert.Equal("notifications", snapshot.Channel);
        Assert.Equal(9, snapshot.UserId);
        Assert.Equal(2, snapshot.SubscriberCount);

        first.Dispose();
        first.Dispose();
        Assert.Equal(1, Assert.Single(hub.SnapshotLocalSubscribers()).SubscriberCount);

        second.Dispose();
        Assert.Empty(hub.SnapshotLocalSubscribers());
    }

    [Fact]
    public async Task Subscription_and_reader_cancellation_unregister_without_leaking()
    {
        using var hub = new LocalSseHub();
        using var subscriptionCancellation = new CancellationTokenSource();
        var cancelledSubscription = hub.Subscribe(
            "notifications",
            10,
            subscriptionCancellation.Token);

        subscriptionCancellation.Cancel();
        Assert.Empty(hub.SnapshotLocalSubscribers());
        await using (var cancelledReader = cancelledSubscription
                         .Reader(TestContext.Current.CancellationToken)
                         .GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            Assert.False(await cancelledReader.MoveNextAsync());
        }

        using var alreadyCancelled = new CancellationTokenSource();
        alreadyCancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            hub.Subscribe("notifications", 11, alreadyCancelled.Token));
        Assert.Empty(hub.SnapshotLocalSubscribers());

        var readerSubscription = hub.Subscribe("notifications", 12, CancellationToken.None);
        using var readerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        await using (var reader = readerSubscription
                         .Reader(readerCancellation.Token)
                         .GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            var pendingRead = reader.MoveNextAsync().AsTask();
            readerCancellation.Cancel();
            Assert.False(await pendingRead.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken));
        }

        Assert.Empty(hub.SnapshotLocalSubscribers());
    }

    private static async Task<string> ReadOne(ISseSubscription subscription)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var reader = subscription.Reader(timeout.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        return reader.Current;
    }

    private static async Task<IReadOnlyList<string>> ReadExactly(
        ISseSubscription subscription,
        int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var reader = subscription.Reader(timeout.Token).GetAsyncEnumerator();
        var result = new List<string>(count);

        for (var index = 0; index < count; index++)
        {
            Assert.True(await reader.MoveNextAsync());
            result.Add(reader.Current);
        }

        return result;
    }

    private sealed class Payload
    {
        public int Id { get; init; }
        public string Label { get; init; } = string.Empty;
        public int PublicField;
    }
}
