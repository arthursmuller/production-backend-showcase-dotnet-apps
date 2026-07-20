using Feed.PerformanceTests;
using PerformanceTesting;

return await PerformanceRunner.RunAsync(
    args,
    WorkloadKind.Load,
    FeedPerformanceScenario.Definition);
