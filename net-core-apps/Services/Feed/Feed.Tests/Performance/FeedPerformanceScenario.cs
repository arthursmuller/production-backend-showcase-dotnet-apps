using PerformanceTesting;

namespace Feed.PerformanceTests;

internal static class FeedPerformanceScenario
{
    internal static PerformanceScenario Definition { get; } = new(
        Domain: "Feed",
        DefaultBaseUrl: new Uri("http://127.0.0.1:5006", UriKind.Absolute),
        DefaultPath: "/api/feed/user/auth/items?pageSize=20&includeSeen=true");
}
