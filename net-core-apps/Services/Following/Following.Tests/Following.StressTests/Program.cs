using Following.PerformanceTests;
using PerformanceTesting;

return await PerformanceRunner.RunAsync(
    args,
    WorkloadKind.Stress,
    FollowingPerformanceScenario.Definition);
