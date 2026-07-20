using PerformanceTesting;

namespace Moderation.PerformanceTests;

internal static class ModerationPerformanceScenario
{
    // The safe default exercises readiness only. Representative business-route runs must
    // explicitly provide PERF_PATH, PERF_ACCESS_TOKEN and, for writes, PERF_ALLOW_MUTATIONS.
    internal static PerformanceScenario Definition { get; } = new(
        Domain: "Moderation",
        DefaultBaseUrl: new Uri("http://127.0.0.1:5020", UriKind.Absolute),
        DefaultPath: "/health/ready");
}
