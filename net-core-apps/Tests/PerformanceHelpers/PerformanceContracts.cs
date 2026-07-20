using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace PerformanceTesting;

public enum WorkloadKind
{
    Load,
    Stress,
    Soak,
}

public sealed record PerformanceScenario(
    string Domain,
    Uri DefaultBaseUrl,
    string DefaultPath);

internal static class PerformanceLimits
{
    public const int MaximumOperations = 32;
    public const int MaximumOperationNameLength = 64;
    public const int MaximumOperationWeight = 10_000;
    public const int MaximumTotalWeight = 100_000;
    public const int MaximumPathLength = 2_048;
    public const int MaximumBodyBytes = 262_144;
    public const int MaximumContentTypeLength = 128;
    public const int MaximumAccessTokenLength = 8_192;
}

internal enum ArrivalModelKind
{
    PacedFixedRate,
    LinearConcurrencyRampWithRecovery,
    PeriodicBurst,
}

internal sealed class PerformanceConfigurationException : Exception
{
    public PerformanceConfigurationException(string message) : base(message)
    {
    }
}

internal sealed class PerformanceProfileDocument
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    public Dictionary<string, PerformanceOperationProfile> Operations { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, PerformanceProfile> Profiles { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class PerformanceOperationProfile
{
    public string? Path { get; init; }
    public string Method { get; init; } = "GET";
    public string? Body { get; init; }
    public string ContentType { get; init; } = "application/json";
    public bool RequiresAuthentication { get; init; }
}

internal sealed class PerformanceMixEntry
{
    public string? Operation { get; init; }
    public int Weight { get; init; }
}

internal sealed class PerformanceProfile
{
    public bool Enabled { get; init; }
    public string? ArrivalModel { get; init; }
    public int? DurationSeconds { get; init; }
    public int? RecoverySeconds { get; init; }
    public int? StartConcurrency { get; init; }
    public int? Concurrency { get; init; }
    public double? TargetRps { get; init; }
    public int? RequestTimeoutSeconds { get; init; }
    public int? BurstEverySeconds { get; init; }
    public int? BurstDurationSeconds { get; init; }
    public double? BurstMultiplier { get; init; }
    public List<PerformanceMixEntry> Mix { get; init; } = [];
}

internal sealed record PerformanceOperation(
    string Name,
    int Weight,
    string RequestPath,
    HttpMethod Method,
    string? RequestBody,
    string ContentType,
    bool RequiresAuthentication)
{
    public bool IsMutation => Method != HttpMethod.Get && Method != HttpMethod.Head;
}

internal sealed record PerformanceRunConfiguration(
    string RunId,
    string ProfileName,
    string TargetEnvironment,
    Uri BaseUrl,
    IReadOnlyList<PerformanceOperation> Operations,
    ArrivalModelKind ArrivalModel,
    string? AccessToken,
    string OutputDirectory,
    int DurationSeconds,
    int RecoverySeconds,
    int StartConcurrency,
    int Concurrency,
    double TargetRps,
    int RequestTimeoutSeconds,
    int? BurstEverySeconds,
    int? BurstDurationSeconds,
    double? BurstMultiplier,
    PerformanceThresholds Thresholds,
    OperationalObservations OperationalObservations);

internal sealed record PerformanceThresholds(
    double? MaximumErrorRatePercent,
    double? MaximumP95Milliseconds,
    double? MinimumThroughputRps,
    double? MaximumFreshnessLagMilliseconds,
    long? MaximumBacklogCount)
{
    public bool IsConfigured =>
        MaximumErrorRatePercent.HasValue ||
        MaximumP95Milliseconds.HasValue ||
        MinimumThroughputRps.HasValue ||
        MaximumFreshnessLagMilliseconds.HasValue ||
        MaximumBacklogCount.HasValue;
}

internal sealed record OperationalObservations(
    double? DependencySaturationPercent,
    double? TargetCpuPercent,
    double? TargetWorkingSetMegabytes,
    double? FreshnessLagMilliseconds,
    long? BacklogCount);

internal sealed record GateCheck(
    string Name,
    bool Passed,
    string Actual,
    string Expected);

internal sealed record GateEvaluation(
    bool Configured,
    bool Passed,
    IReadOnlyList<GateCheck> Checks);

internal sealed record RunnerResources(
    double CpuPercent,
    double PeakWorkingSetMegabytes);

internal sealed record ArrivalModelSummary(
    string Kind,
    string Scheduling,
    double InitialTargetRequestsPerSecond,
    double PeakTargetRequestsPerSecond,
    int? BurstEverySeconds,
    int? BurstDurationSeconds,
    double? BurstMultiplier);

internal sealed record OperationPerformanceSummary(
    string Name,
    string Method,
    int ConfiguredWeight,
    long TotalRequests,
    long SuccessfulRequests,
    long FailedRequests,
    double ErrorRatePercent,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds);

internal sealed record PerformanceSummary(
    [property: JsonPropertyName("$schema")] string Schema,
    string SchemaVersion,
    string Domain,
    string Workload,
    string Profile,
    string RunId,
    string TargetEnvironment,
    string TargetAuthority,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    double DurationSeconds,
    int ConfiguredDurationSeconds,
    int ConfiguredRecoverySeconds,
    int StartConcurrency,
    int MaximumConcurrency,
    double TargetRequestsPerSecond,
    ArrivalModelSummary ArrivalModel,
    long TotalRequests,
    long SuccessfulRequests,
    long FailedRequests,
    double ThroughputRequestsPerSecond,
    double ErrorRatePercent,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    IReadOnlyDictionary<int, long> StatusCodes,
    IReadOnlyDictionary<string, long> Failures,
    IReadOnlyList<OperationPerformanceSummary> Operations,
    RunnerResources RunnerResources,
    OperationalObservations OperationalObservations,
    GateEvaluation Gate);

internal sealed class PerformanceRunStatistics
{
    private readonly IReadOnlyDictionary<string, PerformanceStatistics> _operations;

    public PerformanceRunStatistics(
        IReadOnlyList<PerformanceOperation> operations,
        int requestTimeoutMilliseconds)
    {
        Total = new PerformanceStatistics(requestTimeoutMilliseconds);
        _operations = operations.ToDictionary(
            operation => operation.Name,
            _ => new PerformanceStatistics(requestTimeoutMilliseconds),
            StringComparer.Ordinal);
    }

    public PerformanceStatistics Total { get; }

    public PerformanceStatistics ForOperation(string operationName) => _operations[operationName];

    public void RecordSuccess(string operationName, int statusCode, TimeSpan latency)
    {
        Total.RecordSuccess(statusCode, latency);
        ForOperation(operationName).RecordSuccess(statusCode, latency);
    }

    public void RecordFailure(
        string operationName,
        int? statusCode,
        string failure,
        TimeSpan latency)
    {
        Total.RecordFailure(statusCode, failure, latency);
        ForOperation(operationName).RecordFailure(statusCode, failure, latency);
    }
}

internal sealed class PerformanceStatistics
{
    private readonly long[] _latencyHistogram;
    private readonly ConcurrentDictionary<int, long> _statusCodes = new();
    private readonly ConcurrentDictionary<string, long> _failures = new(StringComparer.Ordinal);
    private long _total;
    private long _successful;
    private long _failed;

    public PerformanceStatistics(int requestTimeoutMilliseconds)
    {
        var upperBound = Math.Clamp(requestTimeoutMilliseconds, 1_000, 300_000);
        _latencyHistogram = new long[upperBound + 1];
    }

    public long Total => Interlocked.Read(ref _total);
    public long Successful => Interlocked.Read(ref _successful);
    public long Failed => Interlocked.Read(ref _failed);

    public void RecordSuccess(int statusCode, TimeSpan latency)
    {
        RecordLatency(latency);
        Interlocked.Increment(ref _total);
        Interlocked.Increment(ref _successful);
        _statusCodes.AddOrUpdate(statusCode, 1, static (_, current) => current + 1);
    }

    public void RecordFailure(int? statusCode, string failure, TimeSpan latency)
    {
        RecordLatency(latency);
        Interlocked.Increment(ref _total);
        Interlocked.Increment(ref _failed);

        if (statusCode.HasValue)
            _statusCodes.AddOrUpdate(statusCode.Value, 1, static (_, current) => current + 1);

        _failures.AddOrUpdate(failure, 1, static (_, current) => current + 1);
    }

    public double Percentile(double percentile)
    {
        var total = Total;
        if (total == 0)
            return 0;

        var target = Math.Max(1, (long)Math.Ceiling(total * percentile));
        long observed = 0;

        for (var milliseconds = 0; milliseconds < _latencyHistogram.Length; milliseconds++)
        {
            observed += Interlocked.Read(ref _latencyHistogram[milliseconds]);
            if (observed >= target)
                return milliseconds;
        }

        return _latencyHistogram.Length - 1;
    }

    public IReadOnlyDictionary<int, long> SnapshotStatusCodes() =>
        new SortedDictionary<int, long>(_statusCodes);

    public IReadOnlyDictionary<string, long> SnapshotFailures() =>
        new SortedDictionary<string, long>(_failures, StringComparer.Ordinal);

    private void RecordLatency(TimeSpan latency)
    {
        var milliseconds = (int)Math.Clamp(
            Math.Ceiling(latency.TotalMilliseconds),
            0,
            _latencyHistogram.Length - 1);
        Interlocked.Increment(ref _latencyHistogram[milliseconds]);
    }
}

internal sealed record ProcessSnapshot(
    TimeSpan TotalProcessorTime,
    long PeakWorkingSetBytes)
{
    public static ProcessSnapshot Capture()
    {
        using var process = Process.GetCurrentProcess();
        return new ProcessSnapshot(process.TotalProcessorTime, process.PeakWorkingSet64);
    }
}
