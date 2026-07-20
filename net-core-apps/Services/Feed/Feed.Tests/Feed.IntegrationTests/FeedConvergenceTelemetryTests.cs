using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Feed.Domain.AggregatesModel;
using Feed.Infra.Persistence;
using Xunit;

namespace Feed.UnitTests;

public sealed class FeedConvergenceTelemetryTests
{
    private static readonly DateTime Source =
        new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Convergence_metrics_use_exact_budgets_and_only_bounded_dimensions()
    {
        var doubles = new ConcurrentQueue<DoubleMeasurement>();
        var longs = new ConcurrentQueue<LongMeasurement>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (KnownNames.Contains(instrument.Name))
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            doubles.Enqueue(new(instrument.Name, value, Copy(tags))));
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            longs.Enqueue(new(instrument.Name, value, Copy(tags))));
        listener.Start();

        var feed = new FeedTelemetry();
        var audience = new AudienceProjectionTelemetry();
        feed.RecordActivityConvergence(Source, Source.AddSeconds(60));
        feed.RecordActivityConvergence(Source, Source.AddMilliseconds(60_001));
        audience.RecordConvergence("event", Source, Source.AddSeconds(60));
        audience.RecordConvergence("snapshot", Source, Source.AddMilliseconds(60_001));
        feed.RecordRevocationCompletion(
            FeedRevocationKinds.Relationship,
            Source,
            Source.AddSeconds(5));
        feed.RecordRevocationCompletion(
            FeedRevocationKinds.Block,
            Source,
            Source.AddMilliseconds(5_001));

        Assert.Contains(doubles, measurement =>
            measurement.Name == FeedTelemetry.ActivityConvergenceDurationName
            && Near(measurement.Value, 60));
        Assert.Contains(doubles, measurement =>
            measurement.Name == FeedTelemetry.ActivityConvergenceDurationName
            && Near(measurement.Value, 60.001));
        var activityBreach = Assert.Single(
            longs,
            measurement => measurement.Name == FeedTelemetry.ActivityConvergenceBreachName);
        Assert.Equal(1, activityBreach.Value);
        Assert.Empty(activityBreach.Tags);

        Assert.Contains(doubles, measurement =>
            measurement.Name == AudienceProjectionTelemetry.ConvergenceDurationName
            && Near(measurement.Value, 60)
            && measurement.Tags.GetValueOrDefault("projection.source") == "event");
        var audienceBreach = Assert.Single(
            longs,
            measurement => measurement.Name == AudienceProjectionTelemetry.ConvergenceBreachName);
        Assert.Equal(1, audienceBreach.Value);
        Assert.Equal("snapshot", audienceBreach.Tags.GetValueOrDefault("projection.source"));

        Assert.Contains(doubles, measurement =>
            measurement.Name == FeedTelemetry.RevocationCompletionDurationName
            && Near(measurement.Value, 5)
            && measurement.Tags.GetValueOrDefault("revocation.kind")
                == FeedRevocationKinds.Relationship);
        var revocationBreach = Assert.Single(
            longs,
            measurement => measurement.Name == FeedTelemetry.RevocationCompletionBreachName);
        Assert.Equal(1, revocationBreach.Value);
        Assert.Equal(
            FeedRevocationKinds.Block,
            revocationBreach.Tags.GetValueOrDefault("revocation.kind"));

        Assert.All(
            doubles.Where(measurement => measurement.Name.Contains(
                "convergence",
                StringComparison.Ordinal)
                || measurement.Name.Contains("revocation.completion", StringComparison.Ordinal)),
            measurement => Assert.All(
                measurement.Tags.Keys,
                key => Assert.Contains(key, new[] { "projection.source", "revocation.kind" })));
    }

    private static readonly HashSet<string> KnownNames =
    [
        FeedTelemetry.ActivityConvergenceDurationName,
        FeedTelemetry.ActivityConvergenceBreachName,
        AudienceProjectionTelemetry.ConvergenceDurationName,
        AudienceProjectionTelemetry.ConvergenceBreachName,
        FeedTelemetry.RevocationCompletionDurationName,
        FeedTelemetry.RevocationCompletionBreachName,
    ];

    private static Dictionary<string, string> Copy(
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in tags)
            copy[tag.Key] = tag.Value?.ToString() ?? string.Empty;
        return copy;
    }

    private static bool Near(double actual, double expected) =>
        Math.Abs(actual - expected) < 0.000_01;

    private sealed record DoubleMeasurement(
        string Name,
        double Value,
        IReadOnlyDictionary<string, string> Tags);

    private sealed record LongMeasurement(
        string Name,
        long Value,
        IReadOnlyDictionary<string, string> Tags);
}
