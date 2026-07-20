using Feed.PerformanceTests;
using PerformanceTesting;

return await PerformanceRunner.RunAsync(
    args,
    WorkloadKind.Soak,
    FeedPerformanceScenario.Definition);
