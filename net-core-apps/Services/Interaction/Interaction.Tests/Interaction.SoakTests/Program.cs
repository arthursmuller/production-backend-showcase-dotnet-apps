using Interaction.PerformanceTests;
using PerformanceTesting;

return await PerformanceRunner.RunAsync(args, WorkloadKind.Soak, InteractionPerformanceScenario.Definition);
