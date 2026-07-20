using Comment.PerformanceTests;
using PerformanceTesting;

return await PerformanceRunner.RunAsync(
    args,
    WorkloadKind.Soak,
    CommentPerformanceScenario.Definition);
