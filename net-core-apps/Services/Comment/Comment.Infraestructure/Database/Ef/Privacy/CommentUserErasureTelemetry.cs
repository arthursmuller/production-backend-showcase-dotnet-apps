using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Inspacy.Observability;

namespace Comment.Infra.Persistence.Privacy;

public sealed class CommentUserErasureTelemetry
{
    private static readonly Meter Meter = PlatformInstrumentation.Meter;
    private static readonly Counter<long> Schedules =
        Meter.CreateCounter<long>("inspacy.comment.user_erasure.schedules");
    private static readonly Counter<long> Cycles =
        Meter.CreateCounter<long>("inspacy.comment.user_erasure.cycles");
    private static readonly Counter<long> Rows =
        Meter.CreateCounter<long>("inspacy.comment.user_erasure.rows");

    public void Schedule(string outcome) =>
        Schedules.Add(1, new KeyValuePair<string, object?>("erasure.outcome", outcome));

    public void Cycle(string outcome) =>
        Cycles.Add(1, new KeyValuePair<string, object?>("erasure.outcome", outcome));

    public void Row(string stage, int count) =>
        Rows.Add(count, new KeyValuePair<string, object?>("erasure.stage", stage));
}
