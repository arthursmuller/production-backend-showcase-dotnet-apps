using Comment.Domain.Exceptions;
using Comment.Infra.Providers.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using StarterEntities.Storage;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Comment.UnitTests;

public sealed class MediaSecurityScannerTests
{
    private static readonly byte[] ValidPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
    ];

    [Fact]
    public async Task Clean_content_is_fully_verified_hashed_and_rewound()
    {
        var scanner = Scanner(requireMalwareScan: true, MalwareScanVerdict.Clean);
        await using var content = new MemoryStream(ValidPng);

        var verified = await scanner.VerifyAsync(
            File("avatar.png", "image/png", ValidPng, MediaType.Image),
            content,
            TestContext.Current.CancellationToken);

        Assert.Equal("avatar.png", verified.FileName);
        Assert.Equal("image/png", verified.MimeType);
        Assert.Equal(".png", verified.Extension);
        Assert.Equal(ValidPng.Length, verified.SizeBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(ValidPng)), verified.Sha256);
        Assert.Equal(0, content.Position);
    }

    [Theory]
    [InlineData("avatar.jpg", "image/png")]
    [InlineData("avatar.png", "image/jpeg")]
    public async Task Spoofed_extension_or_mime_is_rejected(string fileName, string mimeType)
    {
        var scanner = Scanner(requireMalwareScan: false, MalwareScanVerdict.Clean);
        await using var content = new MemoryStream(ValidPng);

        await Assert.ThrowsAsync<CommentException>(() => scanner.VerifyAsync(
            File(fileName, mimeType, ValidPng, MediaType.Image),
            content,
            TestContext.Current.CancellationToken));
        Assert.Equal(0, content.Position);
    }

    [Fact]
    public async Task Declared_length_mismatch_is_rejected()
    {
        var scanner = Scanner(requireMalwareScan: false, MalwareScanVerdict.Clean);
        await using var content = new MemoryStream(ValidPng);
        var file = new IncomingMediaFile(
            "avatar.png",
            "image/png",
            ValidPng.Length + 1,
            MediaType.Image,
            () => new MemoryStream(ValidPng));

        await Assert.ThrowsAsync<CommentException>(() => scanner.VerifyAsync(
            file,
            content,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Path_bearing_filename_is_rejected()
    {
        var scanner = Scanner(requireMalwareScan: false, MalwareScanVerdict.Clean);
        await using var content = new MemoryStream(ValidPng);

        await Assert.ThrowsAsync<CommentException>(() => scanner.VerifyAsync(
            File("folder/avatar.png", "image/png", ValidPng, MediaType.Image),
            content,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Image_decompression_bomb_dimensions_are_rejected()
    {
        var oversizedPng = ValidPng.ToArray();
        oversizedPng[16] = 0x00;
        oversizedPng[17] = 0x00;
        oversizedPng[18] = 0x23;
        oversizedPng[19] = 0x28; // width 9000
        var scanner = Scanner(requireMalwareScan: false, MalwareScanVerdict.Clean);
        await using var content = new MemoryStream(oversizedPng);

        var error = await Assert.ThrowsAsync<CommentException>(() => scanner.VerifyAsync(
            File("wide.png", "image/png", oversizedPng, MediaType.Image),
            content,
            TestContext.Current.CancellationToken));

        Assert.Contains("dimensions", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Malware_verdict_rejects_content_with_unprocessable_status()
    {
        var scanner = Scanner(requireMalwareScan: true, MalwareScanVerdict.Infected);
        await using var content = new MemoryStream(ValidPng);

        var error = await Assert.ThrowsAsync<CommentException>(() => scanner.VerifyAsync(
            File("avatar.png", "image/png", ValidPng, MediaType.Image),
            content,
            TestContext.Current.CancellationToken));

        Assert.Equal(422, error.StatusCode);
        Assert.DoesNotContain("avatar", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scanner_dependency_failure_is_fail_closed_with_service_unavailable_status()
    {
        var options = ValidOptions(requireMalwareScan: true);
        var scanner = CreateScanner(options, new ThrowingClamAvClient());
        await using var content = new MemoryStream(ValidPng);

        var error = await Assert.ThrowsAsync<CommentException>(() => scanner.VerifyAsync(
            File("avatar.png", "image/png", ValidPng, MediaType.Image),
            content,
            TestContext.Current.CancellationToken));

        Assert.Equal(503, error.StatusCode);
        Assert.Equal(0, content.Position);
    }

    [Fact]
    public async Task Saturated_replica_sheds_scan_load_without_queueing()
    {
        var options = ValidOptions(requireMalwareScan: true);
        options.MaximumConcurrentScansPerReplica = 1;
        var client = new BlockingClamAvClient();
        var scanner = CreateScanner(options, client);
        var first = VerifyValidAsync(scanner, TestContext.Current.CancellationToken);
        await client.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        try
        {
            var error = await Assert.ThrowsAsync<CommentException>(() =>
                VerifyValidAsync(scanner, TestContext.Current.CancellationToken));

            Assert.Equal(503, error.StatusCode);
            Assert.Equal(1, Volatile.Read(ref client.ScanCalls));
        }
        finally
        {
            client.Release.TrySetResult();
            await first;
        }
    }

    [Fact]
    public async Task Open_breaker_allows_one_half_open_probe_and_closes_after_recovery()
    {
        var options = ValidOptions(requireMalwareScan: true);
        options.MaximumConcurrentScansPerReplica = 2;
        options.CircuitBreakerFailureThreshold = 2;
        options.CircuitBreakerOpenSeconds = 10;
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero));
        var client = new RecoveringClamAvClient();
        var scanner = CreateScanner(options, client, clock);

        Assert.Equal(503, (await Assert.ThrowsAsync<CommentException>(() =>
            VerifyValidAsync(scanner, TestContext.Current.CancellationToken))).StatusCode);
        Assert.Equal(503, (await Assert.ThrowsAsync<CommentException>(() =>
            VerifyValidAsync(scanner, TestContext.Current.CancellationToken))).StatusCode);
        Assert.Equal(503, (await Assert.ThrowsAsync<CommentException>(() =>
            VerifyValidAsync(scanner, TestContext.Current.CancellationToken))).StatusCode);
        Assert.Equal(2, Volatile.Read(ref client.ScanCalls));

        clock.Advance(TimeSpan.FromSeconds(11));
        var halfOpen = VerifyValidAsync(scanner, TestContext.Current.CancellationToken);
        await client.HalfOpenStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        try
        {
            var rejected = await Assert.ThrowsAsync<CommentException>(() =>
                VerifyValidAsync(scanner, TestContext.Current.CancellationToken));
            Assert.Equal(503, rejected.StatusCode);
            Assert.Equal(3, Volatile.Read(ref client.ScanCalls));
        }
        finally
        {
            client.HalfOpenRelease.TrySetResult();
            await halfOpen;
        }

        await VerifyValidAsync(scanner, TestContext.Current.CancellationToken);
        Assert.Equal(4, Volatile.Read(ref client.ScanCalls));
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved_and_does_not_trip_breaker()
    {
        var options = ValidOptions(requireMalwareScan: true);
        options.CircuitBreakerFailureThreshold = 1;
        var client = new CancellationThenSuccessClamAvClient();
        var scanner = CreateScanner(options, client);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var first = VerifyValidAsync(scanner, cancellation.Token);
        await client.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        await VerifyValidAsync(scanner, TestContext.Current.CancellationToken);
        Assert.Equal(2, Volatile.Read(ref client.ScanCalls));
    }

    [Fact]
    public async Task Dependency_timeout_is_mapped_to_service_unavailable()
    {
        var options = ValidOptions(requireMalwareScan: true);
        var scanner = CreateScanner(options, new TimeoutClamAvClient());

        var error = await Assert.ThrowsAsync<CommentException>(() =>
            VerifyValidAsync(scanner, TestContext.Current.CancellationToken));

        Assert.Equal(503, error.StatusCode);
    }

    [Fact]
    public async Task Readiness_probes_owned_role_and_does_not_expose_dependency_details()
    {
        var options = ValidOptions(requireMalwareScan: true);
        var client = new SensitiveThrowingClamAvClient();
        var health = new MediaSecurityReadinessHealthCheck(
            options,
            new ClamAvResiliencePolicy(options, client),
            Configuration("Api"));

        var result = await health.CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
        Assert.DoesNotContain("clamav.private", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("3310", result.Description, StringComparison.Ordinal);
        Assert.Equal(1, Volatile.Read(ref client.ProbeCalls));
    }

    [Fact]
    public async Task Readiness_is_healthy_only_after_a_successful_dependency_probe()
    {
        var options = ValidOptions(requireMalwareScan: true);
        var client = new CountingClamAvClient();
        var health = new MediaSecurityReadinessHealthCheck(
            options,
            new ClamAvResiliencePolicy(options, client),
            Configuration("JobWorker"));

        var result = await health.CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, Volatile.Read(ref client.ProbeCalls));
    }

    [Fact]
    public async Task Readiness_skips_roles_that_do_not_own_media_workflow()
    {
        var options = ValidOptions(requireMalwareScan: true);
        var client = new CountingClamAvClient();
        var health = new MediaSecurityReadinessHealthCheck(
            options,
            new ClamAvResiliencePolicy(options, client),
            Configuration("ConsumerWorker"));

        var result = await health.CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(0, Volatile.Read(ref client.ProbeCalls));
    }

    [Theory]
    [InlineData(nameof(MediaSecurityOptions.MaximumConcurrentScansPerReplica), 0)]
    [InlineData(nameof(MediaSecurityOptions.MaximumConcurrentScansPerReplica), 65)]
    [InlineData(nameof(MediaSecurityOptions.CircuitBreakerFailureThreshold), 0)]
    [InlineData(nameof(MediaSecurityOptions.CircuitBreakerFailureThreshold), 21)]
    [InlineData(nameof(MediaSecurityOptions.CircuitBreakerOpenSeconds), 0)]
    [InlineData(nameof(MediaSecurityOptions.CircuitBreakerOpenSeconds), 301)]
    [InlineData(nameof(MediaSecurityOptions.ReadinessProbeTimeoutSeconds), 0)]
    [InlineData(nameof(MediaSecurityOptions.ReadinessProbeTimeoutSeconds), 11)]
    public void Resilience_options_reject_unsafe_or_unbounded_values(
        string propertyName,
        int value)
    {
        var options = ValidOptions(requireMalwareScan: true);
        typeof(MediaSecurityOptions).GetProperty(propertyName)!.SetValue(options, value);

        Assert.False(options.IsValid("Production"));
    }

    [Fact]
    public void Production_configuration_pins_scanner_resilience_bounds()
    {
        var repository = FindRepositoryRoot();
        using var document = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(
            repository,
            "net-core-apps",
            "Services",
            "Comment",
            "Comment.Api",
            "appsettings.json")));
        var media = document.RootElement.GetProperty("Storage").GetProperty("MediaSecurity");

        Assert.Equal(4, media.GetProperty("MaximumConcurrentScansPerReplica").GetInt32());
        Assert.Equal(3, media.GetProperty("CircuitBreakerFailureThreshold").GetInt32());
        Assert.Equal(30, media.GetProperty("CircuitBreakerOpenSeconds").GetInt32());
        Assert.Equal(2, media.GetProperty("ReadinessProbeTimeoutSeconds").GetInt32());

        var production = System.IO.File.ReadAllText(Path.Combine(
            repository,
            "net-core-apps",
            "DevOps",
            "environments",
            "production",
            "comment.values.example.yaml"));
        Assert.Contains(
            "Storage__MediaSecurity__MaximumConcurrentScansPerReplica: \"4\"",
            production,
            StringComparison.Ordinal);
        Assert.Contains(
            "Storage__MediaSecurity__CircuitBreakerFailureThreshold: \"3\"",
            production,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Production_options_cannot_disable_scanning()
    {
        var options = ValidOptions(requireMalwareScan: false);

        Assert.False(options.IsValid("Production"));
        Assert.True(options.IsValid("Development"));
    }

    [Fact]
    public void Svg_is_not_an_allowed_image_payload()
    {
        var error = Assert.Throws<CommentException>(() =>
            Comment.Domain.AggregatesModel.CommentMediaRules.EnsureValid(
                "payload.svg",
                "image/svg+xml",
                128,
                MediaType.Image));

        Assert.Contains("does not match", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static MediaSecurityScanner Scanner(
        bool requireMalwareScan,
        MalwareScanVerdict verdict)
    {
        var options = ValidOptions(requireMalwareScan);
        return CreateScanner(options, new StubClamAvClient(verdict));
    }

    private static MediaSecurityScanner CreateScanner(
        MediaSecurityOptions options,
        IClamAvClient client,
        TimeProvider? timeProvider = null) => new(
        options,
        new ClamAvResiliencePolicy(options, client, timeProvider),
        NullLogger<MediaSecurityScanner>.Instance,
        timeProvider);

    private static MediaSecurityOptions ValidOptions(bool requireMalwareScan) => new()
    {
        RequireMalwareScan = requireMalwareScan,
        ClamAvHost = "clamav.media-security.svc.cluster.local",
        ClamAvPort = 3310,
        ConnectTimeoutSeconds = 2,
        ScanTimeoutSeconds = 30,
        ChunkBytes = 16 * 1024,
        MaximumConcurrentScansPerReplica = 4,
        CircuitBreakerFailureThreshold = 3,
        CircuitBreakerOpenSeconds = 30,
        ReadinessProbeTimeoutSeconds = 2,
    };

    private static IncomingMediaFile File(
        string name,
        string mime,
        byte[] bytes,
        MediaType mediaType) => new(
        name,
        mime,
        bytes.Length,
        mediaType,
        () => new MemoryStream(bytes));

    private static async Task VerifyValidAsync(
        MediaSecurityScanner scanner,
        CancellationToken cancellationToken)
    {
        await using var content = new MemoryStream(ValidPng);
        _ = await scanner.VerifyAsync(
            File("avatar.png", "image/png", ValidPng, MediaType.Image),
            content,
            cancellationToken);
    }

    private static IConfiguration Configuration(string role) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Runtime:Role"] = role,
            }).Build();

    private static MalwareScanResult CleanResult() => new(
        MalwareScanVerdict.Clean,
        "ClamAV",
        "1.4.3",
        "27422");

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (System.IO.File.Exists(Path.Combine(
                current.FullName,
                "net-core-apps",
                "Services",
                "Comment",
                "Comment.Api",
                "appsettings.json")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("The repository root could not be located.");
    }

    private sealed class StubClamAvClient(MalwareScanVerdict verdict) : IClamAvClient
    {
        public Task<MalwareScanResult> ScanAsync(
            Stream content,
            CancellationToken cancellationToken) => Task.FromResult(new MalwareScanResult(
                verdict,
                "ClamAV",
                "1.4.3",
                "27422"));

        public Task ProbeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ThrowingClamAvClient : IClamAvClient
    {
        public Task<MalwareScanResult> ScanAsync(
            Stream content,
            CancellationToken cancellationToken) =>
            throw new IOException("simulated scanner dependency failure");

        public Task ProbeAsync(CancellationToken cancellationToken) =>
            throw new IOException("simulated scanner dependency failure");
    }

    private sealed class BlockingClamAvClient : IClamAvClient
    {
        public readonly TaskCompletionSource Started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public int ScanCalls;

        public async Task<MalwareScanResult> ScanAsync(
            Stream content,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref ScanCalls);
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return CleanResult();
        }

        public Task ProbeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecoveringClamAvClient : IClamAvClient
    {
        public readonly TaskCompletionSource HalfOpenStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource HalfOpenRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public int ScanCalls;

        public async Task<MalwareScanResult> ScanAsync(
            Stream content,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref ScanCalls);
            if (call <= 2)
                throw new IOException("simulated dependency outage");
            if (call == 3)
            {
                HalfOpenStarted.TrySetResult();
                await HalfOpenRelease.Task.WaitAsync(cancellationToken);
            }
            return CleanResult();
        }

        public Task ProbeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CancellationThenSuccessClamAvClient : IClamAvClient
    {
        public readonly TaskCompletionSource Started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public int ScanCalls;

        public async Task<MalwareScanResult> ScanAsync(
            Stream content,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref ScanCalls);
            if (call == 1)
            {
                Started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return CleanResult();
        }

        public Task ProbeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TimeoutClamAvClient : IClamAvClient
    {
        public Task<MalwareScanResult> ScanAsync(
            Stream content,
            CancellationToken cancellationToken) =>
            Task.FromException<MalwareScanResult>(new OperationCanceledException());

        public Task ProbeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SensitiveThrowingClamAvClient : IClamAvClient
    {
        public int ProbeCalls;

        public Task<MalwareScanResult> ScanAsync(
            Stream content,
            CancellationToken cancellationToken) =>
            Task.FromResult(CleanResult());

        public Task ProbeAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref ProbeCalls);
            throw new IOException("clamav.private.svc.cluster.local:3310 refused connection");
        }
    }

    private sealed class CountingClamAvClient : IClamAvClient
    {
        public int ProbeCalls;

        public Task<MalwareScanResult> ScanAsync(
            Stream content,
            CancellationToken cancellationToken) =>
            Task.FromResult(CleanResult());

        public Task ProbeAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref ProbeCalls);
            return Task.CompletedTask;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow += duration;
    }
}
