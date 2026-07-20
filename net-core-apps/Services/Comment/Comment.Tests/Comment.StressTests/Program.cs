using Comment.PerformanceTests;
using PerformanceTesting;

return await PerformanceRunner.RunAsync(
    args,
    WorkloadKind.Stress,
    CommentPerformanceScenario.Definition);
