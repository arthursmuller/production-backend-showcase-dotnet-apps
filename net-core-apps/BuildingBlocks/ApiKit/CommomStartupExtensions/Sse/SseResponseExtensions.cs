using System.Collections.Generic;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CommomStartupExtensions.Sse
{
    /// <summary>
    /// HTTP helpers + DI registration for SSE.
    ///
    /// Endpoints invoke <see cref="ServerSentEvents"/> from a minimal-API
    /// route handler and return the resulting <see cref="IResult"/>. The
    /// connection stays open until the client disconnects or the request is
    /// cancelled. Framing/flushing/Content-Type/Cache-Control is handled by
    /// the .NET 10 native SSE writer
    /// (<see cref="TypedResults.ServerSentEvents{T}(IAsyncEnumerable{SseItem{T}}, string?)"/>),
    /// shared by every server-sent event endpoint.
    /// </summary>
    public static class SseResponseExtensions
    {
        /// <summary>
        /// Registers the bounded <see cref="LocalSseHub"/> singleton.
        ///
        /// <para>Delivery is per-replica, ephemeral, and best-effort. A publish
        /// reaches only matching connections held by the current process;
        /// events are not persisted or replayed. Slow subscribers retain the
        /// newest bounded window and drop their oldest queued event.</para>
        ///
        /// <para>Clients must fetch authoritative state when the synthetic
        /// <c>connect</c> event is received on every initial connection or
        /// reconnection. Cross-replica real-time delivery belongs in a
        /// dedicated gateway/backplane, not this shared HTTP helper.</para>
        ///
        /// </summary>
        public static IServiceCollection AddSseHub(this IServiceCollection services)
        {
            services.TryAddSingleton<LocalSseHub>();
            services.TryAddSingleton<ISseHub>(sp => sp.GetRequiredService<LocalSseHub>());
            return services;
        }

        /// <summary>
        /// Returns an <see cref="IResult"/> that streams every payload emitted
        /// by <paramref name="hub"/> on <paramref name="channel"/> for
        /// <paramref name="userId"/> as Server-Sent-Events frames using the
        /// .NET 10 native SSE writer.
        /// </summary>
        /// <remarks>
        /// The first frame is a synthetic <c>connect</c> event so subscribers
        /// can refresh state on (re)connection. Subsequent frames are emitted
        /// as <c>message</c> events with the raw payload string as <c>data</c>.
        /// </remarks>
        public static IResult ServerSentEvents(
            this ISseHub hub,
            string channel,
            int userId,
            CancellationToken ct)
            => TypedResults.ServerSentEvents(StreamItems(hub, channel, userId, ct));

        private static async IAsyncEnumerable<SseItem<string>> StreamItems(
            ISseHub hub,
            string channel,
            int userId,
            [EnumeratorCancellation] CancellationToken ct)
        {
            using var sub = hub.Subscribe(channel, userId, ct);

            // This is a resynchronization signal, not a replay boundary. The
            // client must pull authoritative state missed while disconnected.
            yield return new SseItem<string>("connected", eventType: "connect");

            await foreach (var payload in sub.Reader(ct).WithCancellation(ct))
            {
                yield return new SseItem<string>(payload, eventType: "message");
            }
        }
    }
}
