using PerformanceTesting;

namespace Interaction.PerformanceTests;

internal static class InteractionPerformanceScenario
{
    internal static PerformanceScenario Definition { get; } = new(
        Domain: "Interaction",
        DefaultBaseUrl: new Uri("http://127.0.0.1:5010", UriKind.Absolute),
        DefaultPath: "/api/resources/comment/performance-fixture/engagement");
}
