using System.Text;
using System.Text.Json;

namespace PerformanceTesting;

internal sealed record PerformanceOutputPaths(string JsonPath, string MarkdownPath);

internal static class PerformanceReportWriter
{
    public static async Task<PerformanceOutputPaths> WriteAsync(
        string outputDirectory,
        PerformanceSummary summary,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var prefix = string.Join(
            '-',
            summary.Domain.ToLowerInvariant(),
            summary.Workload,
            summary.Profile,
            summary.RunId);
        var jsonPath = Path.Combine(outputDirectory, $"{prefix}.json");
        var markdownPath = Path.Combine(outputDirectory, $"{prefix}.md");

        await using (var stream = File.Create(jsonPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                summary,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                },
                cancellationToken);
        }

        await File.WriteAllTextAsync(
            markdownPath,
            BuildMarkdown(summary),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        return new PerformanceOutputPaths(jsonPath, markdownPath);
    }

    private static string BuildMarkdown(PerformanceSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {summary.Domain} {summary.Workload} performance summary");
        builder.AppendLine();
        builder.AppendLine($"- Run: `{summary.RunId}`");
        builder.AppendLine($"- Profile: `{summary.Profile}`");
        builder.AppendLine($"- Environment: `{summary.TargetEnvironment}`");
        builder.AppendLine($"- Target: `{summary.TargetAuthority}`");
        builder.AppendLine($"- Window: {summary.StartedAtUtc:O} to {summary.FinishedAtUtc:O}");
        builder.AppendLine($"- Arrival model: `{summary.ArrivalModel.Kind}` ({summary.ArrivalModel.Scheduling})");
        builder.AppendLine();
        builder.AppendLine("| Metric | Value |");
        builder.AppendLine("|---|---:|");
        builder.AppendLine($"| Requests | {summary.TotalRequests} |");
        builder.AppendLine($"| Successful | {summary.SuccessfulRequests} |");
        builder.AppendLine($"| Failed | {summary.FailedRequests} |");
        builder.AppendLine($"| Throughput | {summary.ThroughputRequestsPerSecond:F2} req/s |");
        builder.AppendLine($"| Error rate | {summary.ErrorRatePercent:F2}% |");
        builder.AppendLine($"| p50 | {summary.P50Milliseconds:F0} ms |");
        builder.AppendLine($"| p95 | {summary.P95Milliseconds:F0} ms |");
        builder.AppendLine($"| p99 | {summary.P99Milliseconds:F0} ms |");
        builder.AppendLine($"| Runner CPU | {summary.RunnerResources.CpuPercent:F2}% |");
        builder.AppendLine($"| Runner peak working set | {summary.RunnerResources.PeakWorkingSetMegabytes:F2} MiB |");
        builder.AppendLine($"| Dependency saturation | {Format(summary.OperationalObservations.DependencySaturationPercent, "%")} |");
        builder.AppendLine($"| Target CPU | {Format(summary.OperationalObservations.TargetCpuPercent, "%")} |");
        builder.AppendLine($"| Target working set | {Format(summary.OperationalObservations.TargetWorkingSetMegabytes, " MiB")} |");
        builder.AppendLine($"| Freshness lag | {Format(summary.OperationalObservations.FreshnessLagMilliseconds, " ms")} |");
        builder.AppendLine($"| Backlog | {Format(summary.OperationalObservations.BacklogCount)} |");
        builder.AppendLine();
        builder.AppendLine("## Arrival schedule");
        builder.AppendLine();
        builder.AppendLine("| Measure | Value |");
        builder.AppendLine("|---|---:|");
        builder.AppendLine($"| Model | {summary.ArrivalModel.Kind} |");
        builder.AppendLine($"| Initial target | {summary.ArrivalModel.InitialTargetRequestsPerSecond:F2} req/s |");
        builder.AppendLine($"| Peak target | {summary.ArrivalModel.PeakTargetRequestsPerSecond:F2} req/s |");
        builder.AppendLine($"| Burst period | {Format(summary.ArrivalModel.BurstEverySeconds, " s")} |");
        builder.AppendLine($"| Burst duration | {Format(summary.ArrivalModel.BurstDurationSeconds, " s")} |");
        builder.AppendLine($"| Burst multiplier | {Format(summary.ArrivalModel.BurstMultiplier, "x")} |");
        builder.AppendLine();
        builder.AppendLine("## Operation outcomes");
        builder.AppendLine();
        builder.AppendLine("Operation paths, bodies, and authentication material are intentionally excluded.");
        builder.AppendLine();
        builder.AppendLine("| Operation | Method | Weight | Requests | Success | Errors | Error rate | p50 | p95 | p99 |");
        builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var operation in summary.Operations)
        {
            builder.AppendLine(
                $"| {operation.Name} | {operation.Method} | {operation.ConfiguredWeight} | " +
                $"{operation.TotalRequests} | {operation.SuccessfulRequests} | {operation.FailedRequests} | " +
                $"{operation.ErrorRatePercent:F2}% | {operation.P50Milliseconds:F0} ms | " +
                $"{operation.P95Milliseconds:F0} ms | {operation.P99Milliseconds:F0} ms |");
        }

        builder.AppendLine();
        builder.AppendLine("## Gate");
        builder.AppendLine();

        if (!summary.Gate.Configured)
        {
            builder.AppendLine("No SLO thresholds were configured; only runner-validity checks apply.");
        }

        builder.AppendLine(summary.Gate.Passed ? "Passed." : "Failed.");
        builder.AppendLine();
        builder.AppendLine("| Check | Actual | Expected | Result |");
        builder.AppendLine("|---|---:|---:|---|");
        foreach (var check in summary.Gate.Checks)
        {
            builder.AppendLine(
                $"| {check.Name} | {check.Actual} | {check.Expected} | {(check.Passed ? "pass" : "fail")} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Response outcomes");
        builder.AppendLine();
        if (summary.StatusCodes.Count == 0)
        {
            builder.AppendLine("No HTTP response was received.");
        }
        else
        {
            foreach (var pair in summary.StatusCodes)
                builder.AppendLine($"- HTTP {pair.Key}: {pair.Value}");
        }

        if (summary.Failures.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Failures");
            builder.AppendLine();
            foreach (var pair in summary.Failures)
                builder.AppendLine($"- {pair.Key}: {pair.Value}");
        }

        return builder.ToString();
    }

    private static string Format(double? value, string suffix) =>
        value.HasValue ? $"{value.Value:F2}{suffix}" : "not supplied";

    private static string Format(int? value, string suffix) =>
        value.HasValue ? $"{value.Value}{suffix}" : "not supplied";

    private static string Format(long? value) =>
        value.HasValue ? value.Value.ToString() : "not supplied";
}
