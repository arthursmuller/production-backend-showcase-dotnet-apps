using PerformanceTesting;

namespace Comment.PerformanceTests;

internal static class CommentPerformanceScenario
{
    internal static PerformanceScenario Definition { get; } = new(
        Domain: "Comment",
        DefaultBaseUrl: new Uri("http://127.0.0.1:5014", UriKind.Absolute),
        DefaultPath: "/api/comments/list?PageSize=10");
}
