using System.Diagnostics.Metrics;
using Feed.Infra.Persistence;
using Xunit;

namespace Feed.IntegrationTests;

public sealed class FeedUserErasureMetricsTests
{
    [Fact]
    public void Failure_metric_uses_the_exported_platform_meter()
    {
        long failures = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == "Inspacy.Platform"
                && instrument.Name == "feed.user_erasure.failures")
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
            Interlocked.Add(ref failures, measurement));
        listener.Start();

        new FeedUserErasureMetrics().Failure();

        Assert.Equal(1, Volatile.Read(ref failures));
    }
}
