using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace MessageBrokerExtensions;

/// <summary>
/// Evaluates only the monitor's in-memory snapshot. Readiness probes never add database load.
/// </summary>
public sealed class MassTransitOutboxBacklogHealthCheck<TContext> : IHealthCheck
    where TContext : DbContext
{
    private readonly IMassTransitOutboxBacklogState<TContext> _state;
    private readonly OutboxTelemetryOptions _options;
    private readonly TimeProvider _timeProvider;

    public MassTransitOutboxBacklogHealthCheck(
        IMassTransitOutboxBacklogState<TContext> state,
        IOptions<MassTransitBrokerOptions> options,
        TimeProvider timeProvider)
    {
        _state = state;
        _options = options.Value.OutboxTelemetry;
        _timeProvider = timeProvider;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_options.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "MassTransit outbox readiness sampling is explicitly disabled for this role."));
        }

        var snapshot = _state.Current;
        if (snapshot.LastSuccessfulSampleUtc is null)
        {
            var neverAttempted = snapshot.LastAttemptUtc == DateTimeOffset.MinValue;
            return Task.FromResult(HealthCheckResult.Unhealthy(
                neverAttempted
                    ? "The MassTransit outbox has not been sampled yet."
                    : "The MassTransit outbox query failed before any successful sample."));
        }

        var now = _timeProvider.GetUtcNow();
        var sampleAgeSeconds = Math.Max(
            0,
            (now - snapshot.LastSuccessfulSampleUtc.Value).TotalSeconds);
        var effectiveOldestAgeSeconds = snapshot.PendingMessages == 0
            ? 0
            : snapshot.OldestMessageAgeSeconds + sampleAgeSeconds;
        var data = new Dictionary<string, object>
        {
            ["sample_state"] = snapshot.SampleSucceeded ? "succeeded" : "query_failed",
            ["sample_age_seconds"] = sampleAgeSeconds,
            ["pending_messages"] = snapshot.PendingMessages,
            ["oldest_message_age_seconds"] = effectiveOldestAgeSeconds,
            ["maximum_pending_messages"] = _options.MaximumPendingMessages,
            ["maximum_oldest_message_age_seconds"] = _options.MaximumOldestMessageAgeSeconds,
            ["maximum_sample_age_seconds"] = _options.MaximumSampleAgeSeconds,
        };

        if (sampleAgeSeconds > _options.MaximumSampleAgeSeconds)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "The last successful MassTransit outbox sample is stale.",
                data: data));
        }

        if (snapshot.PendingMessages > _options.MaximumPendingMessages)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "The MassTransit outbox pending-message safety bound was exceeded.",
                data: data));
        }

        if (effectiveOldestAgeSeconds > _options.MaximumOldestMessageAgeSeconds)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "The MassTransit outbox oldest-message safety bound was exceeded.",
                data: data));
        }

        if (!snapshot.SampleSucceeded)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "The latest MassTransit outbox query failed; a recent last-good sample remains inside the bounded grace window.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "The MassTransit outbox backlog and sampler are within their configured safety bounds.",
            data));
    }
}
