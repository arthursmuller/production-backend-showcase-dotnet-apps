using System.Collections.Generic;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
    /// replacing the legacy hand-rolled <c>WriteSseStream</c> helper.
    /// </summary>
    public static class SseResponseExtensions
    {
        /// <summary>
        /// Registers <see cref="ISseHub"/> backed by <see cref="RabbitMqSseHub"/>
        /// so SSE events fan out across every API replica via a fanout exchange
        /// (per-pod exclusive auto-delete queue). Required for multi-replica
        /// Kubernetes deployments.
        ///
        /// <para>The same singleton instance is exposed both as <see cref="ISseHub"/>
        /// and as an <see cref="IHostedService"/> so the broker consumer is
        /// started exactly once at boot, against the same instance that
        /// services resolve for publishing.</para>
        ///
        /// <para>The caller is responsible for registering
        /// <c>IRabbitMQPersistentConnection</c> beforehand (typically via
        /// <c>RMQConfigureConnection</c>).</para>
        ///
        /// </summary>
        public static IServiceCollection AddSseHub(this IServiceCollection services)
        {
            services.TryAddSingleton<RabbitMqSseHub>();
            services.TryAddSingleton<ISseHub>(sp => sp.GetRequiredService<RabbitMqSseHub>());
            services.AddHostedService(sp => sp.GetRequiredService<RabbitMqSseHub>());
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

            // Initial "connect" event lets the client onConnect callback fire
            // immediately so it can pull any state missed while disconnected.
            yield return new SseItem<string>("connected", eventType: "connect");

            await foreach (var payload in sub.Reader(ct).WithCancellation(ct))
            {
                yield return new SseItem<string>(payload, eventType: "message");
            }
        }
    }
}
