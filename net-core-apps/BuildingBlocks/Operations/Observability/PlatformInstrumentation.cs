using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Inspacy.Observability;

/// <summary>
/// Company-owned instrumentation identity shared by platform and service code.
/// Metric and span names remain owned by the component that records them.
/// </summary>
public static class PlatformInstrumentation
{
    public const string Name = "Inspacy.Platform";
    public const string ActivitySourceName = Name;
    public const string MeterName = Name;

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);
}
