using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CommomStartupExtensions.Sse
{
    /// <summary>
    /// Server-Sent-Events fan-out hub.
    ///
    /// Per <c>(channel, userId)</c> pair the hub keeps a bounded in-memory queue
    /// per subscriber on the current replica. Each long-lived <c>/stream</c>
    /// HTTP request becomes one subscriber
    /// that dequeues and writes <c>data: ...\n\n</c> frames to the response
    /// body.
    ///
    /// <para><see cref="LocalSseHub"/> is deliberately replica-local,
    /// ephemeral, and best-effort. Clients must fetch authoritative state on
    /// every connection or reconnection. Cross-replica real-time fan-out is a
    /// dedicated gateway/backplane responsibility.</para>
    /// </summary>
    public interface ISseHub
    {
        /// <summary>Publish <paramref name="data"/> to matching active subscribers on the current replica.</summary>
        Task Publish(
            string channel,
            int userId,
            object data,
            CancellationToken cancellationToken);

        /// <summary>Subscribe to events for <paramref name="channel"/> and <paramref name="userId"/>; the returned subscription is disposable.</summary>
        ISseSubscription Subscribe(string channel, int userId, CancellationToken ct);

        /// <summary>
        /// Diagnostics: snapshot of the current replica's active subscribers as
        /// <c>(channel, userId, subscriberCount)</c>. Reflects only the
        /// in-process subscriber map; other replicas report their own.
        /// </summary>
        IReadOnlyList<SseSubscriberSnapshot> SnapshotLocalSubscribers();
    }

    /// <summary>Single per-(channel,userId) entry returned by <see cref="ISseHub.SnapshotLocalSubscribers"/>.</summary>
    public sealed class SseSubscriberSnapshot
    {
        public string Channel { get; set; } = string.Empty;
        public int UserId { get; set; }
        public int SubscriberCount { get; set; }
    }
}
