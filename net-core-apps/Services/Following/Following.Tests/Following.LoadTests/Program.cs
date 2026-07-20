using Following.PerformanceTests;
using PerformanceTesting;

return await PerformanceRunner.RunAsync(
    args,
    WorkloadKind.Load,
    FollowingPerformanceScenario.Definition);
