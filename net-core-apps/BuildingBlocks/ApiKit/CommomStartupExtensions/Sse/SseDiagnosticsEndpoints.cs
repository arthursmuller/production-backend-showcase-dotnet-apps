using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CommomStartupExtensions.Sse
{
    /// <summary>
    /// TEMP diagnostic endpoints for the SSE pipeline. Mounted from each API
    /// that uses <see cref="ISseHub"/>:
    ///
    /// <list type="bullet">
    ///   <item><description><c>POST /api/_diag/sse/publish?channel=...&amp;userId=...&amp;message=hello</c>
    ///   — calls <see cref="ISseHub.Publish"/> on this pod, fanning the event
    ///   out via RabbitMQ to every replica. Use to verify the SSE delivery
    ///   pipeline end-to-end without depending on the upstream business flow
    ///   (mention, enroll, etc.).</description></item>
    ///   <item><description><c>GET /api/_diag/sse/subscribers</c> — returns the
    ///   list of local-pod subscribers (channel + userId + count). One pod
    ///   only — call against each replica behind the LB to see all of them.</description></item>
    /// </list>
    ///
    /// Both endpoints require role <c>SysAdmin</c>. Remove this file (and the
    /// per-API forwarder route) once SSE delivery is verified.
    /// </summary>
    public static class SseDiagnosticsEndpoints
    {
        public static IEndpointRouteBuilder MapSseDiagnosticsCore(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/_diag/sse").WithTags("_diag-sse");

            group.MapPost("/publish", (
                    [FromServices] ISseHub sse,
                    [FromQuery] string channel,
                    [FromQuery] int userId,
                    [FromQuery] string message) =>
                {
                    if (string.IsNullOrWhiteSpace(channel) || userId <= 0)
                        return Results.BadRequest(new { error = "channel and userId are required" });

                    var payload = new
                    {
                        type = "diagnostic.test",
                        id = Guid.NewGuid().ToString("N"),
                        message = message ?? "diag",
                        sentAt = DateTimeOffset.UtcNow,
                    };
                    _ = sse.Publish(channel, userId, payload);
                    return Results.Ok(new { published = true, channel, userId, payload });
                })
                .RequireAuthorization(p => p.RequireRole("SysAdmin"));

            group.MapGet("/subscribers", ([FromServices] ISseHub sse) =>
                    Results.Ok(new
                    {
                        pod = Environment.MachineName,
                        items = sse.SnapshotLocalSubscribers(),
                    }))
                .RequireAuthorization(p => p.RequireRole("SysAdmin"));

            return routes;
        }
    }
}
