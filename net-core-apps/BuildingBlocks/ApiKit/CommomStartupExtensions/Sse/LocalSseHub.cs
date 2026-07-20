using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace CommomStartupExtensions.Sse;

/// <summary>
/// Bounded, in-process SSE fan-out for connections held by the current replica.
/// Delivery is ephemeral and best-effort: there is no persistence, replay, or
/// cross-replica routing in this component.
/// </summary>
public sealed class LocalSseHub : ISseHub, IDisposable
{
    public const int SubscriberBufferCapacity = 64;
    public const int MaximumChannelLength = 128;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        IncludeFields = true,
        WriteIndented = false
    };

    private readonly ConcurrentDictionary<SubscriberKey, SubscriberGroup> _subscribers = new();
    private int _disposed;

    public Task Publish(
        string channel,
        int userId,
        object data,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = CreateKey(channel, userId);
        ArgumentNullException.ThrowIfNull(data);

        if (Volatile.Read(ref _disposed) != 0)
            return Task.CompletedTask;

        // Serialize exactly once, before acquiring a subscriber-group lock.
        // Strings are already SSE payloads and must not be JSON-quoted.
        var payload = SerializePayload(data);

        if (!_subscribers.TryGetValue(key, out var group))
            return Task.CompletedTask;

        Subscription[] snapshot;
        lock (group.Gate)
        {
            if (group.Retired)
                return Task.CompletedTask;

            snapshot = group.Subscriptions.ToArray();
        }

        foreach (var subscription in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            subscription.TryPublish(payload);
        }

        return Task.CompletedTask;
    }

    public ISseSubscription Subscribe(string channel, int userId, CancellationToken ct)
    {
        var key = CreateKey(channel, userId);
        ct.ThrowIfCancellationRequested();

        while (true)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            var group = _subscribers.GetOrAdd(key, static _ => new SubscriberGroup());
            Subscription subscription;

            lock (group.Gate)
            {
                // The final disposer retires a group before removing it from the
                // dictionary. A concurrent subscriber that observed that old
                // group retries against the replacement instead of becoming
                // detached from the publish path.
                if (group.Retired)
                    continue;

                subscription = new Subscription(
                    SubscriberBufferCapacity,
                    disposed => Remove(key, group, disposed));
                group.Subscriptions.Add(subscription);
            }

            subscription.AttachCancellation(ct);

            if (Volatile.Read(ref _disposed) != 0)
            {
                subscription.Dispose();
                throw new ObjectDisposedException(nameof(LocalSseHub));
            }

            // Cancellation can race with registration. The registration has
            // already disposed and unregistered the subscription in that case.
            ct.ThrowIfCancellationRequested();
            return subscription;
        }
    }

    public IReadOnlyList<SseSubscriberSnapshot> SnapshotLocalSubscribers()
    {
        var result = new List<SseSubscriberSnapshot>();

        foreach (var entry in _subscribers)
        {
            int count;
            lock (entry.Value.Gate)
            {
                if (entry.Value.Retired)
                    continue;

                count = entry.Value.Subscriptions.Count;
            }

            if (count == 0)
                continue;

            result.Add(new SseSubscriberSnapshot
            {
                Channel = entry.Key.Channel,
                UserId = entry.Key.UserId,
                SubscriberCount = count
            });
        }

        return result;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var entry in _subscribers)
        {
            Subscription[] snapshot;
            lock (entry.Value.Gate)
            {
                entry.Value.Retired = true;
                snapshot = entry.Value.Subscriptions.ToArray();
                entry.Value.Subscriptions.Clear();
            }

            foreach (var subscription in snapshot)
                subscription.Dispose();
        }

        _subscribers.Clear();
    }

    private void Remove(
        SubscriberKey key,
        SubscriberGroup group,
        Subscription subscription)
    {
        lock (group.Gate)
        {
            if (!group.Subscriptions.Remove(subscription) || group.Subscriptions.Count != 0)
                return;

            group.Retired = true;
            _subscribers.TryRemove(key, out _);
        }
    }

    private static SubscriberKey CreateKey(string channel, int userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        var normalizedChannel = channel.Trim();
        if (normalizedChannel.Length > MaximumChannelLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channel),
                channel,
                $"SSE channels cannot exceed {MaximumChannelLength} characters.");
        }

        if (normalizedChannel.Any(char.IsControl))
            throw new ArgumentException("SSE channels cannot contain control characters.", nameof(channel));

        if (userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId), userId, "SSE user IDs must be positive.");

        return new SubscriberKey(normalizedChannel, userId);
    }

    private static string SerializePayload(object data)
    {
        if (data is string payload)
            return payload;

        try
        {
            return JsonSerializer.Serialize(data, data.GetType(), SerializerOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new ArgumentException(
                "The SSE payload must be serializable as JSON.",
                nameof(data),
                exception);
        }
    }

    private readonly record struct SubscriberKey(string Channel, int UserId);

    private sealed class SubscriberGroup
    {
        public object Gate { get; } = new();
        public List<Subscription> Subscriptions { get; } = new();
        public bool Retired { get; set; }
    }

    private sealed class Subscription : ISseSubscription
    {
        private readonly Channel<string> _channel;
        private readonly Action<Subscription> _onDispose;
        private readonly object _lifecycleGate = new();
        private CancellationTokenRegistration _cancellationRegistration;
        private bool _hasCancellationRegistration;
        private bool _disposed;

        public Subscription(int capacity, Action<Subscription> onDispose)
        {
            _onDispose = onDispose;
            _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
        }

        public void AttachCancellation(CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
                return;

            var registration = cancellationToken.Register(
                static state => ((Subscription)state!).Dispose(),
                this);
            var unregister = false;

            lock (_lifecycleGate)
            {
                if (_disposed)
                {
                    unregister = true;
                }
                else
                {
                    _cancellationRegistration = registration;
                    _hasCancellationRegistration = true;
                }
            }

            if (unregister)
                registration.Unregister();
        }

        public bool TryPublish(string payload)
        {
            lock (_lifecycleGate)
                return !_disposed && _channel.Writer.TryWrite(payload);
        }

        public async IAsyncEnumerable<string> Reader(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            try
            {
                while (!IsDisposed)
                {
                    bool hasMore;
                    try
                    {
                        hasMore = await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        yield break;
                    }

                    if (!hasMore || IsDisposed)
                        yield break;

                    while (!IsDisposed && !ct.IsCancellationRequested &&
                           _channel.Reader.TryRead(out var item))
                    {
                        yield return item;
                    }

                    if (ct.IsCancellationRequested)
                        yield break;
                }
            }
            finally
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            CancellationTokenRegistration registration = default;
            var unregister = false;

            lock (_lifecycleGate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                if (_hasCancellationRegistration)
                {
                    registration = _cancellationRegistration;
                    _hasCancellationRegistration = false;
                    unregister = true;
                }
            }

            _channel.Writer.TryComplete();
            _onDispose(this);

            // Unregister is non-blocking, avoiding self-wait when Dispose is
            // invoked by the cancellation callback itself.
            if (unregister)
                registration.Unregister();
        }

        private bool IsDisposed
        {
            get
            {
                lock (_lifecycleGate)
                    return _disposed;
            }
        }
    }
}
