using System.Text.Json;
using PerformanceTesting;

namespace PerformanceTesting.UnitTests;

public sealed class PerformanceArtifactTests
{
    [Fact]
    public async Task Artifact_records_arrival_and_per_operation_percentiles_without_request_secrets()
    {
        const string accessToken = "TOKEN_SENTINEL_MUST_NOT_BE_WRITTEN";
        const string requestBody = "{\"value\":\"BODY_SENTINEL_MUST_NOT_BE_WRITTEN\"}";
        const string requestPath = "/private/PATH_SENTINEL_MUST_NOT_BE_WRITTEN";
        var operation = new PerformanceOperation(
            "write-operation",
            7,
            requestPath,
            HttpMethod.Post,
            requestBody,
            "application/json",
            RequiresAuthentication: true);
        var configuration = Configuration(
            ArrivalModelKind.PacedFixedRate,
            [operation],
            accessToken);
        var statistics = new PerformanceRunStatistics(configuration.Operations, 1_000);
        statistics.RecordSuccess(operation.Name, 200, TimeSpan.FromMilliseconds(10));
        statistics.RecordSuccess(operation.Name, 201, TimeSpan.FromMilliseconds(20));
        statistics.RecordSuccess(operation.Name, 204, TimeSpan.FromMilliseconds(30));
        statistics.RecordFailure(operation.Name, 500, "HTTP 500", TimeSpan.FromMilliseconds(40));
        var started = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        var summary = PerformanceRunner.CreateSummary(
            new PerformanceScenario("Test", new Uri("http://127.0.0.1:5099"), "/health/live"),
            WorkloadKind.Load,
            configuration,
            statistics,
            new ProcessSnapshot(TimeSpan.Zero, 1_024),
            new ProcessSnapshot(TimeSpan.FromMilliseconds(10), 2_048),
            started,
            started.AddSeconds(1),
            TimeSpan.FromSeconds(1));
        var output = Path.Combine(Path.GetTempPath(), $"performance-artifact-{Guid.NewGuid():N}");

        try
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var paths = await PerformanceReportWriter.WriteAsync(output, summary, cancellationToken);
            var json = await File.ReadAllTextAsync(paths.JsonPath, cancellationToken);
            var markdown = await File.ReadAllTextAsync(paths.MarkdownPath, cancellationToken);

            AssertRedacted(json, accessToken, requestBody, requestPath);
            AssertRedacted(markdown, accessToken, requestBody, requestPath);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Assert.Equal("performance-result.schema.json", root.GetProperty("$schema").GetString());
            Assert.Equal("2.0", root.GetProperty("schemaVersion").GetString());
            Assert.Equal("paced-fixed-rate", root.GetProperty("arrivalModel").GetProperty("kind").GetString());
            var result = Assert.Single(root.GetProperty("operations").EnumerateArray().ToArray());
            Assert.Equal("write-operation", result.GetProperty("name").GetString());
            Assert.Equal(4, result.GetProperty("totalRequests").GetInt64());
            Assert.Equal(3, result.GetProperty("successfulRequests").GetInt64());
            Assert.Equal(1, result.GetProperty("failedRequests").GetInt64());
            Assert.Equal(20, result.GetProperty("p50Milliseconds").GetDouble());
            Assert.Equal(40, result.GetProperty("p95Milliseconds").GetDouble());
            Assert.Equal(40, result.GetProperty("p99Milliseconds").GetDouble());
            Assert.Contains("## Operation outcomes", markdown);
            Assert.Contains("write-operation", markdown);
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void Load_stress_and_soak_arrival_summaries_and_soak_burst_rates_are_distinct()
    {
        var operation = new PerformanceOperation(
            "read",
            1,
            "/health/live",
            HttpMethod.Get,
            null,
            "application/json",
            false);
        var load = Configuration(ArrivalModelKind.PacedFixedRate, [operation], null);
        var stress = Configuration(
            ArrivalModelKind.LinearConcurrencyRampWithRecovery,
            [operation],
            null) with
        {
            StartConcurrency = 2,
            Concurrency = 10,
            RecoverySeconds = 60,
        };
        var soak = Configuration(ArrivalModelKind.PeriodicBurst, [operation], null) with
        {
            BurstEverySeconds = 60,
            BurstDurationSeconds = 10,
            BurstMultiplier = 3,
        };

        var loadSummary = EmptySummary(load, WorkloadKind.Load);
        var stressSummary = EmptySummary(stress, WorkloadKind.Stress);
        var soakSummary = EmptySummary(soak, WorkloadKind.Soak);

        Assert.Equal(10, loadSummary.ArrivalModel.InitialTargetRequestsPerSecond);
        Assert.Equal(10, loadSummary.ArrivalModel.PeakTargetRequestsPerSecond);
        Assert.Equal(2, stressSummary.ArrivalModel.InitialTargetRequestsPerSecond);
        Assert.Equal(10, stressSummary.ArrivalModel.PeakTargetRequestsPerSecond);
        Assert.Equal(10, soakSummary.ArrivalModel.InitialTargetRequestsPerSecond);
        Assert.Equal(30, soakSummary.ArrivalModel.PeakTargetRequestsPerSecond);
        Assert.Equal(10, PerformanceRunner.TargetRateAt(soak, TimeSpan.FromSeconds(20)));
        Assert.Equal(30, PerformanceRunner.TargetRateAt(soak, TimeSpan.FromSeconds(55)));
        Assert.Equal(10, PerformanceRunner.TargetRateAt(soak, TimeSpan.FromSeconds(65)));
    }

    private static PerformanceSummary EmptySummary(
        PerformanceRunConfiguration configuration,
        WorkloadKind workload)
    {
        var now = DateTimeOffset.UtcNow;
        return PerformanceRunner.CreateSummary(
            new PerformanceScenario("Test", new Uri("http://127.0.0.1:5099"), "/health/live"),
            workload,
            configuration,
            new PerformanceRunStatistics(configuration.Operations, 1_000),
            new ProcessSnapshot(TimeSpan.Zero, 0),
            new ProcessSnapshot(TimeSpan.Zero, 0),
            now,
            now.AddSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    private static PerformanceRunConfiguration Configuration(
        ArrivalModelKind arrivalModel,
        IReadOnlyList<PerformanceOperation> operations,
        string? token) => new(
            RunId: "artifact-run",
            ProfileName: "smoke",
            TargetEnvironment: "local",
            BaseUrl: new Uri("http://127.0.0.1:5099/"),
            Operations: operations,
            ArrivalModel: arrivalModel,
            AccessToken: token,
            OutputDirectory: Path.GetTempPath(),
            DurationSeconds: 1,
            RecoverySeconds: 0,
            StartConcurrency: 10,
            Concurrency: 10,
            TargetRps: 10,
            RequestTimeoutSeconds: 1,
            BurstEverySeconds: null,
            BurstDurationSeconds: null,
            BurstMultiplier: null,
            Thresholds: new PerformanceThresholds(null, null, null, null, null),
            OperationalObservations: new OperationalObservations(null, null, null, null, null));

    private static void AssertRedacted(
        string value,
        string accessToken,
        string requestBody,
        string requestPath)
    {
        Assert.DoesNotContain(accessToken, value, StringComparison.Ordinal);
        Assert.DoesNotContain(requestBody, value, StringComparison.Ordinal);
        Assert.DoesNotContain("BODY_SENTINEL_MUST_NOT_BE_WRITTEN", value, StringComparison.Ordinal);
        Assert.DoesNotContain(requestPath, value, StringComparison.Ordinal);
        Assert.DoesNotContain("PATH_SENTINEL_MUST_NOT_BE_WRITTEN", value, StringComparison.Ordinal);
    }
}
