using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace PerformanceTesting;

public static class PerformanceRunner
{
    public static async Task<int> RunAsync(
        string[] args,
        WorkloadKind workload,
        PerformanceScenario scenario)
    {
        PerformanceRunConfiguration configuration;
        try
        {
            configuration = PerformanceConfigurationLoader.Load(args, workload, scenario);
        }
        catch (PerformanceConfigurationException exception)
        {
            Console.Error.WriteLine($"Configuration refused: {exception.Message}");
            return 2;
        }

        Console.WriteLine(
            "Starting {0} {1}/{2} run '{3}' against {4} ({5}) with {6} operations and {7}.",
            scenario.Domain,
            workload.ToString().ToLowerInvariant(),
            configuration.ProfileName,
            configuration.RunId,
            configuration.BaseUrl.GetLeftPart(UriPartial.Authority),
            configuration.TargetEnvironment,
            configuration.Operations.Count,
            PerformanceProfileValidator.ArrivalModelName(configuration.ArrivalModel));

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        var statistics = new PerformanceRunStatistics(
            configuration.Operations,
            configuration.RequestTimeoutSeconds * 1_000);
        var startedAt = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        var processBefore = ProcessSnapshot.Capture();

        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = configuration.Concurrency,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = configuration.BaseUrl,
            Timeout = Timeout.InfiniteTimeSpan,
        };

        try
        {
            await ExecuteAsync(
                client,
                configuration,
                workload,
                statistics,
                shutdown.Token);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            Console.WriteLine("Run cancellation requested; writing the partial result.");
        }

        timer.Stop();
        var finishedAt = DateTimeOffset.UtcNow;
        var processAfter = ProcessSnapshot.Capture();
        var summary = CreateSummary(
            scenario,
            workload,
            configuration,
            statistics,
            processBefore,
            processAfter,
            startedAt,
            finishedAt,
            timer.Elapsed);

        try
        {
            // Preserve a partial result after Ctrl+C, but do not let artifact I/O
            // keep a CI runner alive indefinitely when its filesystem is unhealthy.
            using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var outputs = await PerformanceReportWriter.WriteAsync(
                configuration.OutputDirectory,
                summary,
                reportTimeout.Token);
            Console.WriteLine($"JSON result: {outputs.JsonPath}");
            Console.WriteLine($"Markdown summary: {outputs.MarkdownPath}");
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or OperationCanceledException)
        {
            Console.Error.WriteLine($"Failed to write performance artifacts: {exception.Message}");
            return 3;
        }

        Console.WriteLine(
            "Completed {0} requests at {1:F2} req/s; p95={2:F0} ms; errors={3:F2}%; gate={4}.",
            summary.TotalRequests,
            summary.ThroughputRequestsPerSecond,
            summary.P95Milliseconds,
            summary.ErrorRatePercent,
            summary.Gate.Configured ? (summary.Gate.Passed ? "passed" : "failed") : "informational");

        return summary.Gate.Passed ? 0 : 1;
    }

    private static Task ExecuteAsync(
        HttpClient client,
        PerformanceRunConfiguration configuration,
        WorkloadKind workload,
        PerformanceRunStatistics statistics,
        CancellationToken cancellationToken) => workload switch
        {
            WorkloadKind.Load => ExecuteFixedAsync(client, configuration, statistics, cancellationToken),
            WorkloadKind.Stress => ExecuteStressAsync(client, configuration, statistics, cancellationToken),
            WorkloadKind.Soak => ExecuteFixedAsync(client, configuration, statistics, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(workload)),
        };

    private static async Task ExecuteFixedAsync(
        HttpClient client,
        PerformanceRunConfiguration configuration,
        PerformanceRunStatistics statistics,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var deadline = started + ToStopwatchTicks(TimeSpan.FromSeconds(configuration.DurationSeconds));
        var workers = Enumerable.Range(0, configuration.Concurrency)
            .Select(worker => ExecuteWorkerAsync(
                client,
                configuration,
                statistics,
                worker,
                started,
                started,
                deadline,
                configuration.Concurrency,
                cancellationToken));

        await Task.WhenAll(workers);
    }

    private static async Task ExecuteStressAsync(
        HttpClient client,
        PerformanceRunConfiguration configuration,
        PerformanceRunStatistics statistics,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var rampDuration = TimeSpan.FromSeconds(configuration.DurationSeconds);
        var rampDeadline = started + ToStopwatchTicks(rampDuration);
        var recoveryDeadline = rampDeadline +
            ToStopwatchTicks(TimeSpan.FromSeconds(configuration.RecoverySeconds));

        var workers = Enumerable.Range(0, configuration.Concurrency)
            .Select(async worker =>
            {
                var activationFraction = Math.Max(0, worker - configuration.StartConcurrency + 1d) /
                    Math.Max(1, configuration.Concurrency - configuration.StartConcurrency + 1d);
                var activation = started + (long)(ToStopwatchTicks(rampDuration) * activationFraction);

                var nextSequence = await ExecuteWorkerAsync(
                    client,
                    configuration,
                    statistics,
                    worker,
                    started,
                    activation,
                    rampDeadline,
                    configuration.Concurrency,
                    cancellationToken);

                // Recovery deliberately drops back to the initial worker count and
                // initial aggregate target rate; it does not hold peak RPS with fewer workers.
                if (worker < configuration.StartConcurrency)
                {
                    await ExecuteWorkerAsync(
                        client,
                        configuration,
                        statistics,
                        nextSequence,
                        started,
                        rampDeadline,
                        recoveryDeadline,
                        configuration.Concurrency,
                        cancellationToken);
                }
            });

        await Task.WhenAll(workers);
    }

    private static async Task<long> ExecuteWorkerAsync(
        HttpClient client,
        PerformanceRunConfiguration configuration,
        PerformanceRunStatistics statistics,
        long initialSequence,
        long scheduleStarted,
        long activeFrom,
        long activeUntil,
        int pacingWorkers,
        CancellationToken cancellationToken)
    {
        await DelayUntilAsync(activeFrom, cancellationToken);
        var sequence = initialSequence;

        while (!cancellationToken.IsCancellationRequested && Stopwatch.GetTimestamp() < activeUntil)
        {
            var iterationStarted = Stopwatch.GetTimestamp();
            await SendOnceAsync(client, configuration, statistics, sequence, cancellationToken);
            sequence += configuration.Concurrency;

            var targetRps = TargetRateAt(
                configuration,
                Stopwatch.GetElapsedTime(scheduleStarted, iterationStarted));
            var interval = TimeSpan.FromSeconds(pacingWorkers / targetRps);
            var elapsed = Stopwatch.GetElapsedTime(iterationStarted);
            var delay = interval - elapsed;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
        }

        return sequence;
    }

    internal static double TargetRateAt(
        PerformanceRunConfiguration configuration,
        TimeSpan elapsed)
    {
        if (configuration.ArrivalModel != ArrivalModelKind.PeriodicBurst)
            return configuration.TargetRps;

        var elapsedSeconds = elapsed.TotalSeconds;
        var period = configuration.BurstEverySeconds!.Value;
        var burstDuration = configuration.BurstDurationSeconds!.Value;
        var phase = elapsedSeconds % period;
        var isBurst = phase >= period - burstDuration;
        return isBurst
            ? configuration.TargetRps * configuration.BurstMultiplier!.Value
            : configuration.TargetRps;
    }

    private static async Task SendOnceAsync(
        HttpClient client,
        PerformanceRunConfiguration configuration,
        PerformanceRunStatistics statistics,
        long sequence,
        CancellationToken runToken)
    {
        var operation = WeightedOperationSelector.Select(configuration.Operations, sequence);
        using var request = CreateRequest(configuration, operation, sequence);
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(runToken);
        requestTimeout.CancelAfter(TimeSpan.FromSeconds(configuration.RequestTimeoutSeconds));
        var started = Stopwatch.GetTimestamp();

        try
        {
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                requestTimeout.Token);
            await response.Content.CopyToAsync(Stream.Null, requestTimeout.Token);
            var latency = Stopwatch.GetElapsedTime(started);

            if (response.IsSuccessStatusCode)
            {
                statistics.RecordSuccess(operation.Name, (int)response.StatusCode, latency);
            }
            else
            {
                statistics.RecordFailure(
                    operation.Name,
                    (int)response.StatusCode,
                    $"HTTP {(int)response.StatusCode}",
                    latency);
            }
        }
        catch (OperationCanceledException) when (!runToken.IsCancellationRequested)
        {
            statistics.RecordFailure(
                operation.Name,
                null,
                "Request timeout",
                Stopwatch.GetElapsedTime(started));
        }
        catch (HttpRequestException exception)
        {
            statistics.RecordFailure(
                operation.Name,
                exception.StatusCode.HasValue ? (int)exception.StatusCode.Value : null,
                exception.GetType().Name,
                Stopwatch.GetElapsedTime(started));
        }
    }

    internal static HttpRequestMessage CreateRequest(
        PerformanceRunConfiguration configuration,
        PerformanceOperation operation,
        long sequence)
    {
        var sequenceText = sequence.ToString(CultureInfo.InvariantCulture);
        var sequenceOneText = checked(sequence + 1).ToString(CultureInfo.InvariantCulture);
        var requestPath = ExpandTemplate(
            operation.RequestPath,
            configuration.RunId,
            sequenceText,
            sequenceOneText);
        var request = new HttpRequestMessage(operation.Method, requestPath);
        request.Headers.TryAddWithoutValidation("X-Performance-Run-Id", configuration.RunId);
        request.Headers.TryAddWithoutValidation("X-Performance-Sequence", sequenceText);
        if (operation.IsMutation)
        {
            request.Headers.TryAddWithoutValidation(
                "Idempotency-Key",
                $"perf-{configuration.RunId}-{sequenceText}");
        }

        if (operation.RequiresAuthentication)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.AccessToken);

        if (operation.RequestBody is not null)
        {
            var body = ExpandTemplate(
                operation.RequestBody,
                configuration.RunId,
                sequenceText,
                sequenceOneText);
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(operation.ContentType);
        }

        return request;
    }

    internal static PerformanceSummary CreateSummary(
        PerformanceScenario scenario,
        WorkloadKind workload,
        PerformanceRunConfiguration configuration,
        PerformanceRunStatistics statistics,
        ProcessSnapshot processBefore,
        ProcessSnapshot processAfter,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        TimeSpan elapsed)
    {
        var totals = statistics.Total;
        var durationSeconds = Math.Max(elapsed.TotalSeconds, 0.001);
        var throughput = totals.Total / durationSeconds;
        var errorRate = totals.Total == 0
            ? 100
            : totals.Failed * 100d / totals.Total;
        var observations = configuration.OperationalObservations;
        var p95 = totals.Percentile(0.95);
        var gate = EvaluateGate(
            configuration.Thresholds,
            observations,
            totals.Total,
            totals.Successful,
            errorRate,
            p95,
            throughput);
        var cpuDelta = processAfter.TotalProcessorTime - processBefore.TotalProcessorTime;
        var cpuPercent = cpuDelta.TotalMilliseconds /
            Math.Max(1, elapsed.TotalMilliseconds) /
            Math.Max(1, Environment.ProcessorCount) * 100;

        return new PerformanceSummary(
            "performance-result.schema.json",
            "2.0",
            scenario.Domain,
            workload.ToString().ToLowerInvariant(),
            configuration.ProfileName,
            configuration.RunId,
            configuration.TargetEnvironment,
            configuration.BaseUrl.GetLeftPart(UriPartial.Authority),
            startedAt,
            finishedAt,
            durationSeconds,
            configuration.DurationSeconds,
            configuration.RecoverySeconds,
            configuration.StartConcurrency,
            configuration.Concurrency,
            configuration.TargetRps,
            CreateArrivalModelSummary(configuration),
            totals.Total,
            totals.Successful,
            totals.Failed,
            throughput,
            errorRate,
            totals.Percentile(0.50),
            p95,
            totals.Percentile(0.99),
            totals.SnapshotStatusCodes(),
            totals.SnapshotFailures(),
            configuration.Operations.Select(operation =>
                CreateOperationSummary(operation, statistics.ForOperation(operation.Name))).ToArray(),
            new RunnerResources(
                cpuPercent,
                processAfter.PeakWorkingSetBytes / 1024d / 1024d),
            observations,
            gate);
    }

    private static OperationPerformanceSummary CreateOperationSummary(
        PerformanceOperation operation,
        PerformanceStatistics statistics)
    {
        var errorRate = statistics.Total == 0
            ? 0
            : statistics.Failed * 100d / statistics.Total;
        return new OperationPerformanceSummary(
            operation.Name,
            operation.Method.Method,
            operation.Weight,
            statistics.Total,
            statistics.Successful,
            statistics.Failed,
            errorRate,
            statistics.Percentile(0.50),
            statistics.Percentile(0.95),
            statistics.Percentile(0.99));
    }

    private static ArrivalModelSummary CreateArrivalModelSummary(
        PerformanceRunConfiguration configuration)
    {
        var initialTarget = configuration.ArrivalModel == ArrivalModelKind.LinearConcurrencyRampWithRecovery
            ? configuration.TargetRps * configuration.StartConcurrency / configuration.Concurrency
            : configuration.TargetRps;
        var peakTarget = configuration.ArrivalModel == ArrivalModelKind.PeriodicBurst
            ? configuration.TargetRps * configuration.BurstMultiplier!.Value
            : configuration.TargetRps;
        var scheduling = configuration.ArrivalModel switch
        {
            ArrivalModelKind.PacedFixedRate =>
                "bounded workers paced at a fixed aggregate target",
            ArrivalModelKind.LinearConcurrencyRampWithRecovery =>
                "linear worker activation to peak followed by initial-rate recovery",
            ArrivalModelKind.PeriodicBurst =>
                "bounded workers paced at a base target with recurring end-of-period bursts",
            _ => throw new ArgumentOutOfRangeException(),
        };
        return new ArrivalModelSummary(
            PerformanceProfileValidator.ArrivalModelName(configuration.ArrivalModel),
            scheduling,
            initialTarget,
            peakTarget,
            configuration.BurstEverySeconds,
            configuration.BurstDurationSeconds,
            configuration.BurstMultiplier);
    }

    private static GateEvaluation EvaluateGate(
        PerformanceThresholds thresholds,
        OperationalObservations observations,
        long totalRequests,
        long successfulRequests,
        double errorRate,
        double p95,
        double throughput)
    {
        var checks = new List<GateCheck>
        {
            new(
                "Requests executed",
                totalRequests > 0,
                totalRequests.ToString(CultureInfo.InvariantCulture),
                "> 0"),
            new(
                "Successful responses",
                successfulRequests > 0,
                successfulRequests.ToString(CultureInfo.InvariantCulture),
                "> 0"),
        };
        AddMaximum(checks, "Error rate", errorRate, thresholds.MaximumErrorRatePercent, "%");
        AddMaximum(checks, "p95 latency", p95, thresholds.MaximumP95Milliseconds, " ms");
        AddMinimum(checks, "Throughput", throughput, thresholds.MinimumThroughputRps, " req/s");
        AddMaximum(
            checks,
            "Freshness lag",
            observations.FreshnessLagMilliseconds,
            thresholds.MaximumFreshnessLagMilliseconds,
            " ms");
        AddMaximum(
            checks,
            "Backlog",
            observations.BacklogCount,
            thresholds.MaximumBacklogCount,
            string.Empty);

        return new GateEvaluation(
            thresholds.IsConfigured,
            checks.All(check => check.Passed),
            checks);
    }

    private static void AddMaximum(
        ICollection<GateCheck> checks,
        string name,
        double? actual,
        double? threshold,
        string suffix)
    {
        if (!threshold.HasValue)
            return;
        checks.Add(new GateCheck(
            name,
            actual.HasValue && actual.Value <= threshold.Value,
            actual.HasValue ? $"{actual.Value:F2}{suffix}" : "not supplied",
            $"<= {threshold.Value:F2}{suffix}"));
    }

    private static void AddMaximum(
        ICollection<GateCheck> checks,
        string name,
        long? actual,
        long? threshold,
        string suffix)
    {
        if (!threshold.HasValue)
            return;
        checks.Add(new GateCheck(
            name,
            actual.HasValue && actual.Value <= threshold.Value,
            actual.HasValue ? $"{actual.Value}{suffix}" : "not supplied",
            $"<= {threshold.Value}{suffix}"));
    }

    private static void AddMinimum(
        ICollection<GateCheck> checks,
        string name,
        double? actual,
        double? threshold,
        string suffix)
    {
        if (!threshold.HasValue)
            return;
        checks.Add(new GateCheck(
            name,
            actual.HasValue && actual.Value >= threshold.Value,
            actual.HasValue ? $"{actual.Value:F2}{suffix}" : "not supplied",
            $">= {threshold.Value:F2}{suffix}"));
    }

    private static string ExpandTemplate(
        string value,
        string runId,
        string sequence,
        string sequenceOne) => value
        .Replace("{runId}", runId, StringComparison.Ordinal)
        .Replace("{sequence1}", sequenceOne, StringComparison.Ordinal)
        .Replace("{sequence}", sequence, StringComparison.Ordinal);

    private static async Task DelayUntilAsync(long timestamp, CancellationToken cancellationToken)
    {
        var delay = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), timestamp);
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken);
    }

    private static long ToStopwatchTicks(TimeSpan value) =>
        (long)(value.TotalSeconds * Stopwatch.Frequency);
}
