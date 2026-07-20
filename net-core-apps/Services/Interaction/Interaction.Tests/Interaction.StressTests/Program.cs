using Interaction.PerformanceTests;
using PerformanceTesting;

return await PerformanceRunner.RunAsync(args, WorkloadKind.Stress, InteractionPerformanceScenario.Definition);
