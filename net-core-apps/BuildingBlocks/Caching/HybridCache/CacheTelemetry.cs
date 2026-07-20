using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Inspacy.Caching;

public static class CacheTelemetry
{
    public const string ActivitySourceName = "Inspacy.Caching";
    public const string MeterName = "Inspacy.Caching";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        "inspacy.cache.requests",
        unit: "{request}",
        description: "Cache requests by bounded, low-cardinality operation.");

    private static readonly Counter<long> Hits = Meter.CreateCounter<long>(
        "inspacy.cache.hits",
        unit: "{entry}",
        description: "Hybrid cache requests served without invoking the authoritative source.");

    private static readonly Counter<long> Misses = Meter.CreateCounter<long>(
        "inspacy.cache.misses",
        unit: "{entry}",
        description: "Hybrid cache requests that invoked the authoritative source.");

    private static readonly Counter<long> Fills = Meter.CreateCounter<long>(
        "inspacy.cache.fills",
        unit: "{entry}",
        description: "Successful authoritative-source cache fills.");

    private static readonly Counter<long> Errors = Meter.CreateCounter<long>(
        "inspacy.cache.errors",
        unit: "{error}",
        description: "Cache and authoritative-source errors by bounded layer and operation.");

    private static readonly Counter<long> SourceRejections = Meter.CreateCounter<long>(
        "inspacy.cache.source.rejections",
        unit: "{request}",
        description: "Authoritative-source requests rejected by the cache-zero concurrency budget.");

    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "inspacy.cache.operation.duration",
        unit: "ms",
        description: "Cache operation latency by bounded layer and operation.");

    private static readonly Histogram<long> PayloadSize = Meter.CreateHistogram<long>(
        "inspacy.cache.payload.size",
        unit: "By",
        description: "Serialized L2 cache payload size; cache keys are never dimensions.");

    public static void RecordRequest(string operation) =>
        Requests.Add(1, new KeyValuePair<string, object?>("cache.operation", operation));

    public static void RecordHit() => Hits.Add(1);

    public static void RecordMiss() => Misses.Add(1);

    public static void RecordFill() => Fills.Add(1);

    public static void RecordError(string layer, string operation) => Errors.Add(
        1,
        new KeyValuePair<string, object?>("cache.layer", layer),
        new KeyValuePair<string, object?>("cache.operation", operation));

    public static void RecordSourceRejection() => SourceRejections.Add(1);

    public static void RecordDuration(string layer, string operation, long startedTimestamp) => Duration.Record(
        Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds,
        new KeyValuePair<string, object?>("cache.layer", layer),
        new KeyValuePair<string, object?>("cache.operation", operation));

    public static void RecordPayloadSize(long size) => PayloadSize.Record(size);
}
