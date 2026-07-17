using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessageBrokerCore.Constants;
using MessageBrokerRabbitMq.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CommomStartupExtensions.Sse
{
    /// <summary>
    /// <see cref="ISseHub"/> backed by a RabbitMQ fanout exchange so that SSE
    /// events fan out across every API replica running in Kubernetes.
    ///
    /// <para>How it works:</para>
    /// <list type="number">
    ///   <item><description>On startup the hub declares the fanout exchange
    ///   <see cref="Topology.SseFanoutExchange"/> and creates a per-pod
    ///   exclusive auto-delete queue bound to it. The queue name is generated
    ///   by the broker so every replica gets its own queue and every replica
    ///   receives every message (no consumer-group competition).</description></item>
    ///   <item><description><see cref="Publish"/> does NOT touch the local
    ///   subscriber map directly. It only publishes onto the fanout exchange
    ///   so every replica (including this one) receives the message via the
    ///   single consumer below. This avoids duplicate delivery and guarantees
    ///   the user gets the event regardless of which pod holds their long-lived
    ///   SSE connection.</description></item>
    ///   <item><description>The consumer fans payloads out to in-process
    ///   subscribers keyed by <c>(channel, userId)</c>. Each subscriber owns a bounded
    ///   <see cref="Channel{T}"/> with <see cref="BoundedChannelFullMode.DropOldest"/>
    ///   so a slow client never blocks the consumer thread.</description></item>
    ///   <item><description>Publishing reuses a single long-lived
    ///   <see cref="IModel"/> (guarded by a lock — <c>IModel</c> is not
    ///   thread-safe) instead of opening a new channel per call. This avoids
    ///   exhausting the connection's <c>channel-max</c> under load.</description></item>
    ///   <item><description>Both publisher and consumer channels self-heal:
    ///   on <see cref="IModel.ModelShutdown"/> a background task tears down
    ///   the dead channel and re-declares exchange / queue / binding /
    ///   consumer with exponential backoff.</description></item>
    /// </list>
    /// </summary>
    public class RabbitMqSseHub : ISseHub, IHostedService, IDisposable
    {
        private readonly IRabbitMQPersistentConnection _connection;
        private readonly ILogger<RabbitMqSseHub> _logger;
        private readonly ConcurrentDictionary<string, List<Subscription>> _subscribers
            = new ConcurrentDictionary<string, List<Subscription>>();

        // Consumer side
        private IModel _consumeChannel;
        private string _queueName;

        // Publisher side (long-lived, lock-guarded — IModel is not thread-safe)
        private readonly object _pubSync = new object();
        private IModel _pubChannel;

        private readonly CancellationTokenSource _stopCts = new CancellationTokenSource();
        private bool _stopping;
        private bool _disposed;

        public RabbitMqSseHub(IRabbitMQPersistentConnection connection, ILogger<RabbitMqSseHub> logger)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private static string Key(string channel, int userId) => $"{channel}::{userId}";

        // ---------- ISseHub ----------

        public Task Publish(string channel, int userId, object data)
        {
            if (_stopping) return Task.CompletedTask;

            var payload = data is string s ? s : JsonConvert.SerializeObject(data);
            var envelope = new Envelope { Channel = channel, UserId = userId, Payload = payload };
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));

            try
            {
                lock (_pubSync)
                {
                    var ch = EnsurePublisherChannel_NoLock();
                    if (ch == null) return Task.CompletedTask;

                    var props = ch.CreateBasicProperties();
                    props.DeliveryMode = 1; // transient — SSE is ephemeral
                    ch.BasicPublish(
                        exchange: Topology.SseFanoutExchange,
                        routingKey: string.Empty,
                        mandatory: false,
                        basicProperties: props,
                        body: bytes);
                }

                _logger.LogDebug(
                    "RabbitMqSseHub: published {Bytes}B to {Exchange} for {Channel}/{UserId}",
                    bytes.Length, Topology.SseFanoutExchange, channel, userId);
            }
            catch (Exception ex)
            {
                // Drop the dead channel so the next publish re-declares it.
                lock (_pubSync) { TryDispose(ref _pubChannel); }
                _logger.LogWarning(ex, "RabbitMqSseHub: publish failed for channel {Channel} user {UserId}", channel, userId);
            }

            return Task.CompletedTask;
        }

        public ISseSubscription Subscribe(string channel, int userId, CancellationToken ct)
        {
            var key = Key(channel, userId);
            var ch = Channel.CreateBounded<string>(new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });

            Subscription sub = null;
            sub = new Subscription(ch, () =>
            {
                if (_subscribers.TryGetValue(key, out var l))
                {
                    lock (l)
                    {
                        l.Remove(sub);
                        if (l.Count == 0) _subscribers.TryRemove(key, out _);
                    }
                }
                ch.Writer.TryComplete();
            });

            var list = _subscribers.GetOrAdd(key, _ => new List<Subscription>());
            int total;
            lock (list) { list.Add(sub); total = list.Count; }

            _logger.LogDebug("RabbitMqSseHub: +1 subscriber on {Channel}/{UserId} (now {Total})", channel, userId, total);

            return sub;
        }

        public IReadOnlyList<SseSubscriberSnapshot> SnapshotLocalSubscribers()
        {
            var result = new List<SseSubscriberSnapshot>();
            foreach (var kvp in _subscribers)
            {
                var sep = kvp.Key.IndexOf("::", StringComparison.Ordinal);
                if (sep < 0) continue;
                if (!int.TryParse(kvp.Key.Substring(sep + 2), out var uid)) continue;
                int count;
                lock (kvp.Value) count = kvp.Value.Count;
                result.Add(new SseSubscriberSnapshot
                {
                    Channel = kvp.Key.Substring(0, sep),
                    UserId = uid,
                    SubscriberCount = count,
                });
            }
            return result;
        }

        // ---------- IHostedService ----------

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartConsumerWithRetry();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _stopping = true;
            try { _stopCts.Cancel(); } catch { /* ignore */ }

            lock (_pubSync) { TryDispose(ref _pubChannel); }
            TryDispose(ref _consumeChannel);
            return Task.CompletedTask;
        }

        // ---------- publisher channel lifecycle ----------

        private IModel EnsurePublisherChannel_NoLock()
        {
            if (_pubChannel != null && _pubChannel.IsOpen) return _pubChannel;

            TryDispose(ref _pubChannel);

            if (!_connection.IsConnected && !_connection.TryAndConnect())
            {
                _logger.LogWarning("RabbitMqSseHub: publisher cannot acquire RabbitMQ connection.");
                return null;
            }

            var ch = _connection.CreateModel();
            ch.ExchangeDeclare(Topology.SseFanoutExchange, ExchangeType.Fanout, durable: true, autoDelete: false);
            _pubChannel = ch;
            return _pubChannel;
        }

        // ---------- consumer channel lifecycle (with retry / recovery) ----------

        private void StartConsumerWithRetry()
        {
            _ = Task.Run(async () =>
            {
                var attempt = 0;
                while (!_stopCts.IsCancellationRequested)
                {
                    try
                    {
                        if (TryStartConsumerOnce()) return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "RabbitMqSseHub: failed to start consumer (attempt {Attempt})", attempt + 1);
                    }

                    attempt++;
                    var delaySeconds = Math.Min(60, Math.Pow(2, Math.Min(attempt, 6)));
                    try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), _stopCts.Token); }
                    catch (OperationCanceledException) { return; }
                }
            });
        }

        private bool TryStartConsumerOnce()
        {
            if (!_connection.IsConnected && !_connection.TryAndConnect())
            {
                _logger.LogWarning("RabbitMqSseHub: consumer cannot acquire RabbitMQ connection.");
                return false;
            }

            TryDispose(ref _consumeChannel);

            _consumeChannel = _connection.CreateModel();
            _consumeChannel.ExchangeDeclare(Topology.SseFanoutExchange, ExchangeType.Fanout, durable: true, autoDelete: false);

            // Per-pod exclusive auto-delete queue. Server picks the name. The
            // queue dies with the channel/connection so there is nothing to
            // clean up when the pod is rotated.
            _queueName = _consumeChannel.QueueDeclare(queue: string.Empty, durable: false, exclusive: true, autoDelete: true, arguments: null).QueueName;
            _consumeChannel.QueueBind(_queueName, Topology.SseFanoutExchange, routingKey: string.Empty);

            // If the channel goes down (broker restart, network blip, etc.)
            // restart the whole consumer setup with backoff.
            _consumeChannel.ModelShutdown += OnConsumerChannelShutdown;

            // The shared RabbitMQ connection is created with
            // DispatchConsumersAsync = true (see RabbitMqBrokerExtensions),
            // so consumers MUST be AsyncEventingBasicConsumer — using the
            // sync EventingBasicConsumer throws "In the async mode you have
            // to use an async consumer" on BasicConsume.
            var consumer = new AsyncEventingBasicConsumer(_consumeChannel);
            consumer.Received += OnReceivedAsync;
            _consumeChannel.BasicConsume(_queueName, autoAck: true, consumer);

            _logger.LogInformation("RabbitMqSseHub started. Pod queue: {Queue} bound to {Exchange}", _queueName, Topology.SseFanoutExchange);
            return true;
        }

        private void OnConsumerChannelShutdown(object sender, ShutdownEventArgs e)
        {
            if (_stopping || _disposed) return;
            _logger.LogWarning("RabbitMqSseHub: consumer channel shut down ({ReplyCode} {ReplyText}). Restarting…", e.ReplyCode, e.ReplyText);
            StartConsumerWithRetry();
        }

        // ---------- consumer fan-out to in-process subscribers ----------

        private Task OnReceivedAsync(object sender, BasicDeliverEventArgs ea)
        {
            try
            {
                var json = Encoding.UTF8.GetString(ea.Body.Span);
                var env = JsonConvert.DeserializeObject<Envelope>(json);
                if (env == null || env.Channel == null) return Task.CompletedTask;

                if (!_subscribers.TryGetValue(Key(env.Channel, env.UserId), out var subs))
                {
                    _logger.LogDebug(
                        "RabbitMqSseHub: received envelope for {Channel}/{UserId} but no local subscribers on this pod.",
                        env.Channel, env.UserId);
                    return Task.CompletedTask;
                }

                Subscription[] snapshot;
                lock (subs) snapshot = subs.ToArray();

                _logger.LogDebug(
                    "RabbitMqSseHub: dispatching envelope {Channel}/{UserId} to {Count} local subscriber(s).",
                    env.Channel, env.UserId, snapshot.Length);

                foreach (var sub in snapshot)
                    sub.Writer.TryWrite(env.Payload ?? string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RabbitMqSseHub: failed to dispatch envelope");
            }
            return Task.CompletedTask;
        }

        // ---------- IDisposable ----------

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stopping = true;
            try { _stopCts.Cancel(); } catch { /* ignore */ }
            try { _stopCts.Dispose(); } catch { /* ignore */ }
            lock (_pubSync) { TryDispose(ref _pubChannel); }
            TryDispose(ref _consumeChannel);
        }

        private static void TryDispose(ref IModel ch)
        {
            if (ch == null) return;
            try { if (ch.IsOpen) ch.Close(); } catch { /* ignore */ }
            try { ch.Dispose(); } catch { /* ignore */ }
            ch = null;
        }

        // ---------- helpers ----------

        private sealed class Envelope
        {
            public string Channel { get; set; }
            public int UserId { get; set; }
            public string Payload { get; set; }
        }

        private sealed class Subscription : ISseSubscription
        {
            private readonly Channel<string> _channel;
            private readonly Action _onDispose;
            private bool _disposed;

            public Subscription(Channel<string> ch, Action onDispose)
            {
                _channel = ch;
                _onDispose = onDispose;
            }

            public ChannelWriter<string> Writer => _channel.Writer;

            public async IAsyncEnumerable<string> Reader([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
            {
                // Client disconnects (page refresh, navigation) cancel `ct`,
                // which makes WaitToReadAsync throw OperationCanceledException.
                // That is the expected end-of-stream signal here, NOT an error
                // — yield-break instead of letting it bubble up through the
                // SSE formatter and become a logged exception on every refresh.
                while (true)
                {
                    bool hasMore;
                    try
                    {
                        hasMore = await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        yield break;
                    }

                    if (!hasMore) yield break;

                    while (_channel.Reader.TryRead(out var item))
                        yield return item;
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _onDispose?.Invoke();
            }
        }
    }
}
