using PerformanceTesting;

namespace Following.PerformanceTests;

internal static class FollowingPerformanceScenario
{
    internal static PerformanceScenario Definition { get; } = new(
        Domain: "Following",
        DefaultBaseUrl: new Uri("http://127.0.0.1:5016", UriKind.Absolute),
        DefaultPath: "/health/ready");
}
