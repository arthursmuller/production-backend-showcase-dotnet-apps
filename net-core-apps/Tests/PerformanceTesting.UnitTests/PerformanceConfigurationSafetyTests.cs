using PerformanceTesting;

namespace PerformanceTesting.UnitTests;

public sealed class PerformanceConfigurationSafetyTests
{
    private static readonly PerformanceScenario Scenario = new(
        "Test",
        new Uri("http://127.0.0.1:5099"),
        "/health/live");

    [Fact]
    public void Explicit_execute_is_required()
    {
        using var environment = new PerformanceEnvironmentScope();

        var exception = Assert.Throws<PerformanceConfigurationException>(() =>
            PerformanceConfigurationLoader.Load(
                ["--run-id", "safe-run"],
                WorkloadKind.Load,
                Scenario));

        Assert.Contains("No traffic was sent", exception.Message);
    }

    [Fact]
    public void Non_loopback_target_requires_explicit_opt_in()
    {
        using var environment = new PerformanceEnvironmentScope();

        var exception = Assert.Throws<PerformanceConfigurationException>(() =>
            PerformanceConfigurationLoader.Load(
                [
                    "--execute",
                    "--run-id", "safe-run",
                    "--base-url", "https://performance.example.test",
                ],
                WorkloadKind.Load,
                Scenario));

        Assert.Contains("Non-loopback targets are refused", exception.Message);
    }

    [Fact]
    public void Production_environment_is_always_refused()
    {
        using var environment = new PerformanceEnvironmentScope();
        environment.Set("PERF_ALLOW_NON_LOOPBACK", "true");
        environment.Set("PERF_TARGET_ENVIRONMENT", "production");

        var exception = Assert.Throws<PerformanceConfigurationException>(() =>
            PerformanceConfigurationLoader.Load(
                [
                    "--execute",
                    "--run-id", "safe-run",
                    "--base-url", "https://performance.example.test",
                ],
                WorkloadKind.Load,
                Scenario));

        Assert.Contains("refuses targets identified as production", exception.Message);
    }

    [Fact]
    public void Mutating_mix_requires_a_separate_opt_in()
    {
        using var environment = new PerformanceEnvironmentScope();
        ConfigureDisabledProfile(environment);

        var exception = Assert.Throws<PerformanceConfigurationException>(() =>
            LoadBaseline());

        Assert.Contains("PERF_ALLOW_MUTATIONS=true", exception.Message);
    }

    [Fact]
    public void Authenticated_operation_requires_a_token_after_mutation_is_approved()
    {
        using var environment = new PerformanceEnvironmentScope();
        ConfigureDisabledProfile(environment);
        environment.Set("PERF_ALLOW_MUTATIONS", "true");

        var exception = Assert.Throws<PerformanceConfigurationException>(() =>
            LoadBaseline());

        Assert.Contains("PERF_ACCESS_TOKEN", exception.Message);
    }

    [Fact]
    public async Task Token_is_sent_only_to_operations_declared_authenticated()
    {
        using var environment = new PerformanceEnvironmentScope();
        ConfigureDisabledProfile(environment);
        environment.Set("PERF_ALLOW_MUTATIONS", "true");
        environment.Set("PERF_ACCESS_TOKEN", "test-token-never-persisted");
        var configuration = LoadBaseline();
        var write = Assert.Single(configuration.Operations, operation => operation.IsMutation);
        var safe = Assert.Single(configuration.Operations, operation => !operation.RequiresAuthentication);

        using var writeRequest = PerformanceRunner.CreateRequest(configuration, write, 7);
        using var safeRequest = PerformanceRunner.CreateRequest(configuration, safe, 8);

        Assert.Equal("Bearer", writeRequest.Headers.Authorization?.Scheme);
        Assert.Equal("test-token-never-persisted", writeRequest.Headers.Authorization?.Parameter);
        Assert.Null(safeRequest.Headers.Authorization);
        Assert.Equal("perf-safe-run-7", Assert.Single(writeRequest.Headers.GetValues("Idempotency-Key")));
        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.Contains("safe-run", await writeRequest.Content!.ReadAsStringAsync(cancellationToken));
        Assert.Contains("7", await writeRequest.Content.ReadAsStringAsync(cancellationToken));
    }

    private static PerformanceRunConfiguration LoadBaseline() =>
        PerformanceConfigurationLoader.Load(
            ["--execute", "--run-id", "safe-run", "--profile", "baseline"],
            WorkloadKind.Load,
            Scenario);

    private static void ConfigureDisabledProfile(PerformanceEnvironmentScope environment)
    {
        environment.Set("PERF_DURATION_SECONDS", "1");
        environment.Set("PERF_CONCURRENCY", "1");
        environment.Set("PERF_TARGET_RPS", "1");
        environment.Set("PERF_REQUEST_TIMEOUT_SECONDS", "1");
    }
}

internal sealed class PerformanceEnvironmentScope : IDisposable
{
    private readonly IReadOnlyDictionary<string, string?> _original;

    public PerformanceEnvironmentScope()
    {
        _original = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(entry => entry.Key is string name &&
                name.StartsWith("PERF_", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                entry => (string)entry.Key,
                entry => entry.Value?.ToString(),
                StringComparer.OrdinalIgnoreCase);
        ClearPerformanceVariables();
    }

    public void Set(string name, string value) => Environment.SetEnvironmentVariable(name, value);

    public void Dispose()
    {
        ClearPerformanceVariables();
        foreach (var pair in _original)
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
    }

    private static void ClearPerformanceVariables()
    {
        var names = Environment.GetEnvironmentVariables()
            .Keys
            .Cast<string>()
            .Where(name => name.StartsWith("PERF_", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var name in names)
            Environment.SetEnvironmentVariable(name, null);
    }
}
