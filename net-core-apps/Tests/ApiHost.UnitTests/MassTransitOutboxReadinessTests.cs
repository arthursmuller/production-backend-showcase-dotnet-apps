using MessageBrokerExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApiHost.UnitTests;

public sealed class MassTransitOutboxReadinessTests
{
    private static readonly DateTimeOffset BaselineUtc =
        new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Recent_success_inside_count_and_age_bounds_is_healthy()
    {
        var clock = new ManualTimeProvider(BaselineUtc);
        var state = new FakeState(new MassTransitOutboxBacklogSnapshot(
            PendingMessages: 25,
            OldestMessageAgeSeconds: 10,
            LastSuccessfulSampleUtc: BaselineUtc,
            LastAttemptUtc: BaselineUtc,
            SampleSucceeded: true));
        var health = CreateHealthCheck(state, clock);

        var result = await health.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("succeeded", result.Data["sample_state"]);
        Assert.Equal(25L, result.Data["pending_messages"]);
    }

    [Fact]
    public async Task Pending_count_sentinel_beyond_the_bound_is_unhealthy()
    {
        var clock = new ManualTimeProvider(BaselineUtc);
        var state = new FakeState(new MassTransitOutboxBacklogSnapshot(
            PendingMessages: 100_001,
            OldestMessageAgeSeconds: 10,
            LastSuccessfulSampleUtc: BaselineUtc,
            LastAttemptUtc: BaselineUtc,
            SampleSucceeded: true));

        var result = await CreateHealthCheck(state, clock)
            .CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("pending-message", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oldest_age_advances_between_samples_and_fails_at_the_hard_bound()
    {
        var clock = new ManualTimeProvider(BaselineUtc.AddSeconds(6));
        var state = new FakeState(new MassTransitOutboxBacklogSnapshot(
            PendingMessages: 1,
            OldestMessageAgeSeconds: 295,
            LastSuccessfulSampleUtc: BaselineUtc,
            LastAttemptUtc: BaselineUtc,
            SampleSucceeded: true));

        var result = await CreateHealthCheck(state, clock)
            .CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("oldest-message", result.Description, StringComparison.Ordinal);
        Assert.Equal(301d, result.Data["oldest_message_age_seconds"]);
    }

    [Fact]
    public async Task Stale_last_good_sample_is_unhealthy_even_when_the_retained_backlog_is_empty()
    {
        var clock = new ManualTimeProvider(BaselineUtc.AddSeconds(46));
        var state = new FakeState(new MassTransitOutboxBacklogSnapshot(
            PendingMessages: 0,
            OldestMessageAgeSeconds: 0,
            LastSuccessfulSampleUtc: BaselineUtc,
            LastAttemptUtc: BaselineUtc.AddSeconds(30),
            SampleSucceeded: false));

        var result = await CreateHealthCheck(state, clock)
            .CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("stale", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Initial_and_failed_before_first_success_are_distinct_unhealthy_states()
    {
        var clock = new ManualTimeProvider(BaselineUtc);
        var state = new FakeState(MassTransitOutboxBacklogSnapshot.Initial);
        var health = CreateHealthCheck(state, clock);

        var initial = await health.CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken);
        state.CurrentValue = MassTransitOutboxBacklogSnapshot.Initial with
        {
            LastAttemptUtc = BaselineUtc,
        };
        var failed = await health.CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, initial.Status);
        Assert.Contains("not been sampled", initial.Description, StringComparison.Ordinal);
        Assert.Equal(HealthStatus.Unhealthy, failed.Status);
        Assert.Contains("failed before", failed.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_failure_uses_recent_last_good_as_degraded_then_recovers()
    {
        var clock = new ManualTimeProvider(BaselineUtc);
        var sampler = new QueueSampler(
            _ => Task.FromResult(new MassTransitOutboxBacklogSample(2, 3)),
            _ => Task.FromException<MassTransitOutboxBacklogSample>(new InvalidOperationException("probe")),
            _ => Task.FromResult(new MassTransitOutboxBacklogSample(0, 0)));
        var options = Options.Create(CreateOptions());
        using var monitor = new MassTransitOutboxBacklogMonitor<OutboxProbeDbContext>(
            sampler,
            options,
            clock,
            NullLogger<MassTransitOutboxBacklogMonitor<OutboxProbeDbContext>>.Instance);
        var health = new MassTransitOutboxBacklogHealthCheck<OutboxProbeDbContext>(
            monitor,
            options,
            clock);

        await monitor.SampleOnceAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(5));
        await monitor.SampleOnceAsync(TestContext.Current.CancellationToken);
        var degraded = await health.CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(5));
        await monitor.SampleOnceAsync(TestContext.Current.CancellationToken);
        var recovered = await health.CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Degraded, degraded.Status);
        Assert.Equal("query_failed", degraded.Data["sample_state"]);
        Assert.Equal(HealthStatus.Healthy, recovered.Status);
        Assert.True(monitor.Current.SampleSucceeded);
        Assert.Equal(0, monitor.Current.PendingMessages);
        Assert.Equal(3, sampler.Calls);
    }

    [Fact]
    public async Task Disabled_monitor_never_queries_and_is_explicitly_not_applicable()
    {
        var clock = new ManualTimeProvider(BaselineUtc);
        var sampler = new QueueSampler(
            _ => Task.FromException<MassTransitOutboxBacklogSample>(
                new InvalidOperationException("must not run")));
        var configured = CreateOptions();
        configured.OutboxTelemetry.Enabled = false;
        var options = Options.Create(configured);
        using var monitor = new MassTransitOutboxBacklogMonitor<OutboxProbeDbContext>(
            sampler,
            options,
            clock,
            NullLogger<MassTransitOutboxBacklogMonitor<OutboxProbeDbContext>>.Instance);
        var health = new MassTransitOutboxBacklogHealthCheck<OutboxProbeDbContext>(
            monitor,
            options,
            clock);

        await monitor.SampleOnceAsync(TestContext.Current.CancellationToken);
        var result = await health.CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("disabled", result.Description, StringComparison.Ordinal);
        Assert.Equal(0, sampler.Calls);
    }

    [Fact]
    public async Task Sampling_cancellation_propagates_without_recording_a_false_failure()
    {
        var clock = new ManualTimeProvider(BaselineUtc);
        var sampler = new QueueSampler(cancellationToken =>
            Task.FromCanceled<MassTransitOutboxBacklogSample>(cancellationToken));
        var options = Options.Create(CreateOptions());
        using var monitor = new MassTransitOutboxBacklogMonitor<OutboxProbeDbContext>(
            sampler,
            options,
            clock,
            NullLogger<MassTransitOutboxBacklogMonitor<OutboxProbeDbContext>>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            monitor.SampleOnceAsync(cancellation.Token));

        Assert.Equal(MassTransitOutboxBacklogSnapshot.Initial, monitor.Current);
    }

    private static MassTransitOutboxBacklogHealthCheck<OutboxProbeDbContext> CreateHealthCheck(
        IMassTransitOutboxBacklogState<OutboxProbeDbContext> state,
        TimeProvider timeProvider) =>
        new(state, Options.Create(CreateOptions()), timeProvider);

    private static MassTransitBrokerOptions CreateOptions() => new();

    private abstract class OutboxProbeDbContext : DbContext
    {
    }

    private sealed class FakeState(MassTransitOutboxBacklogSnapshot current) :
        IMassTransitOutboxBacklogState<OutboxProbeDbContext>
    {
        public MassTransitOutboxBacklogSnapshot CurrentValue { get; set; } = current;

        public MassTransitOutboxBacklogSnapshot Current => CurrentValue;
    }

    private sealed class QueueSampler(
        params Func<CancellationToken, Task<MassTransitOutboxBacklogSample>>[] samples) :
        IMassTransitOutboxBacklogSampler<OutboxProbeDbContext>
    {
        private readonly Queue<Func<CancellationToken, Task<MassTransitOutboxBacklogSample>>> _samples =
            new(samples);

        public int Calls { get; private set; }

        public Task<MassTransitOutboxBacklogSample> SampleAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return _samples.Dequeue()(cancellationToken);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
