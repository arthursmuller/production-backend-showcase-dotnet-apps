using Moderation.PerformanceTests;
using PerformanceTesting;

return await PerformanceRunner.RunAsync(args, WorkloadKind.Stress, ModerationPerformanceScenario.Definition);
