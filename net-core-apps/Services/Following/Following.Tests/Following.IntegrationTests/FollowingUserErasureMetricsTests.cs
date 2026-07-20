using System.Diagnostics.Metrics;
using Following.Infra.Persistence;
using Xunit;

namespace Following.IntegrationTests;

public sealed class FollowingUserErasureMetricsTests
{
    [Fact]
    public void Failure_metric_uses_the_exported_platform_meter()
    {
        long failures = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == "Inspacy.Platform"
                && instrument.Name == "following.user_erasure.failures")
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
            Interlocked.Add(ref failures, measurement));
        listener.Start();

        new FollowingUserErasureMetrics().Failure();

        Assert.Equal(1, Volatile.Read(ref failures));
    }
}
