using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CommomStartupExtensions.Sse
{
    /// <summary>
    /// Server-Sent-Events fan-out hub.
    ///
    /// Per <c>(channel, userId)</c> pair the hub keeps an in-memory queue per
    /// pod. Each long-lived <c>/stream</c> HTTP request becomes one subscriber
    /// that dequeues and writes <c>data: ...\n\n</c> frames to the response
    /// body.
    ///
    /// <para><see cref="RabbitMqSseHub"/> uses a RabbitMQ fanout exchange with a
    /// per-pod exclusive auto-delete queue so <see cref="Publish"/> reaches every
    /// replica.</para>
    /// </summary>
    public interface ISseHub
    {
        /// <summary>Publish <paramref name="data"/> to every active subscriber for the given (channel, userId).</summary>
        Task Publish(string channel, int userId, object data);

        /// <summary>Subscribe to events for <paramref name="channel"/> and <paramref name="userId"/>; the returned subscription is disposable.</summary>
        ISseSubscription Subscribe(string channel, int userId, CancellationToken ct);

        /// <summary>
        /// Diagnostics: snapshot of the local pod's active subscribers as
        /// <c>(channel, userId, subscriberCount)</c>. Reflects only the
        /// in-process subscriber map; remote pods report their own.
        /// </summary>
        IReadOnlyList<SseSubscriberSnapshot> SnapshotLocalSubscribers();
    }

    /// <summary>Single per-(channel,userId) entry returned by <see cref="ISseHub.SnapshotLocalSubscribers"/>.</summary>
    public sealed class SseSubscriberSnapshot
    {
        public string Channel { get; set; }
        public int UserId { get; set; }
        public int SubscriberCount { get; set; }
    }
}
