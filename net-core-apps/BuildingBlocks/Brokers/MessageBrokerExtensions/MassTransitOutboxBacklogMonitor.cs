using System.Data;
using System.Diagnostics.Metrics;
using System.Globalization;
using Inspacy.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MessageBrokerExtensions;

public sealed record MassTransitOutboxBacklogSnapshot(
    long PendingMessages,
    double OldestMessageAgeSeconds,
    DateTimeOffset? LastSuccessfulSampleUtc,
    DateTimeOffset LastAttemptUtc,
    bool SampleSucceeded)
{
    public static MassTransitOutboxBacklogSnapshot Initial { get; } = new(
        PendingMessages: 0,
        OldestMessageAgeSeconds: 0,
        LastSuccessfulSampleUtc: null,
        LastAttemptUtc: DateTimeOffset.MinValue,
        SampleSucceeded: false);
}

public sealed record MassTransitOutboxBacklogSample(
    long PendingMessages,
    double OldestMessageAgeSeconds);

public interface IMassTransitOutboxBacklogState<TContext>
    where TContext : DbContext
{
    MassTransitOutboxBacklogSnapshot Current { get; }
}

public interface IMassTransitOutboxBacklogSampler<TContext>
    where TContext : DbContext
{
    Task<MassTransitOutboxBacklogSample> SampleAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads only the oldest configured safe backlog window. The count is exact while it is
/// within the readiness bound; <c>MaximumPendingMessages + 1</c> is a lower-bound breach
/// sentinel. Ordering by the auto-incrementing primary key keeps work index-led and bounded.
/// </summary>
public sealed class MassTransitOutboxBacklogSampler<TContext> :
    IMassTransitOutboxBacklogSampler<TContext>
    where TContext : DbContext
{
    private const string SampleSql =
        "SELECT COUNT(*), " +
        "COALESCE(GREATEST(0, TIMESTAMPDIFF(MICROSECOND, MIN(`SentTime`), UTC_TIMESTAMP(6))) " +
        "/ 1000000.0, 0) " +
        "FROM (SELECT `SentTime` FROM `OutboxMessage` FORCE INDEX (`PRIMARY`) " +
        "ORDER BY `SequenceNumber` LIMIT @sampleLimit) AS `bounded_pending`;";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxTelemetryOptions _options;

    public MassTransitOutboxBacklogSampler(
        IServiceScopeFactory scopeFactory,
        IOptions<MassTransitBrokerOptions> options)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value.OutboxTelemetry;
    }

    public async Task<MassTransitOutboxBacklogSample> SampleAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        await context.Database.OpenConnectionAsync(cancellationToken);

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = SampleSql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = _options.QueryTimeoutSeconds;
        var sampleLimit = command.CreateParameter();
        sampleLimit.ParameterName = "@sampleLimit";
        sampleLimit.DbType = DbType.Int32;
        sampleLimit.Value = checked(_options.MaximumPendingMessages + 1);
        command.Parameters.Add(sampleLimit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The bounded outbox sample returned no row.");

        var pending = reader.GetInt64(0);
        var oldestAge = Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture);
        if (pending < 0 || pending > _options.MaximumPendingMessages + 1L
            || !double.IsFinite(oldestAge) || oldestAge < 0)
        {
            throw new InvalidOperationException("The bounded outbox sample was outside its bounds.");
        }

        return new MassTransitOutboxBacklogSample(
            pending,
            pending == 0 ? 0 : oldestAge);
    }
}

/// <summary>
/// Periodically samples the service-owned MassTransit EF bus outbox. No payload,
/// destination, message identifier, SQL text, connection data, or exception text is
/// exported. The context-type tag has one build-time-bounded value per service store.
/// </summary>
public sealed class MassTransitOutboxBacklogMonitor<TContext> : BackgroundService,
    IMassTransitOutboxBacklogState<TContext>
    where TContext : DbContext
{
    private static readonly Meter Meter = PlatformInstrumentation.Meter;
    private static readonly KeyValuePair<string, object?> StoreTag =
        new("outbox.store", typeof(TContext).Name);

    private readonly IMassTransitOutboxBacklogSampler<TContext> _sampler;
    private readonly OutboxTelemetryOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MassTransitOutboxBacklogMonitor<TContext>> _logger;
    private MassTransitOutboxBacklogSnapshot _snapshot = MassTransitOutboxBacklogSnapshot.Initial;

    public MassTransitOutboxBacklogMonitor(
        IMassTransitOutboxBacklogSampler<TContext> sampler,
        IOptions<MassTransitBrokerOptions> options,
        TimeProvider timeProvider,
        ILogger<MassTransitOutboxBacklogMonitor<TContext>> logger)
    {
        _sampler = sampler;
        _options = options.Value.OutboxTelemetry;
        _timeProvider = timeProvider;
        _logger = logger;

        Meter.CreateObservableGauge(
            "inspacy.messaging.outbox.pending",
            () => new Measurement<long>(Current.PendingMessages, StoreTag),
            unit: "{message}",
            description: "Pending messages, exact through the configured safety bound and capped at bound plus one.");
        Meter.CreateObservableGauge(
            "inspacy.messaging.outbox.oldest_age",
            () => new Measurement<double>(Current.OldestMessageAgeSeconds, StoreTag),
            unit: "s",
            description: "Sampled age of the oldest pending MassTransit EF bus-outbox message.");
        Meter.CreateObservableGauge(
            "inspacy.messaging.outbox.sample_success",
            () => new Measurement<int>(Current.SampleSucceeded ? 1 : 0, StoreTag),
            unit: "{sample}",
            description: "Whether the latest bounded outbox sample succeeded.");
    }

    public MassTransitOutboxBacklogSnapshot Current => Volatile.Read(ref _snapshot);

    public async Task SampleOnceAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return;

        var attemptedAt = _timeProvider.GetUtcNow();

        try
        {
            var sample = await _sampler.SampleAsync(cancellationToken);
            if (sample.PendingMessages < 0
                || sample.PendingMessages > _options.MaximumPendingMessages + 1L
                || !double.IsFinite(sample.OldestMessageAgeSeconds)
                || sample.OldestMessageAgeSeconds < 0)
            {
                throw new InvalidOperationException("The bounded outbox sample was outside its bounds.");
            }

            var completedAt = _timeProvider.GetUtcNow();
            Interlocked.Exchange(
                ref _snapshot,
                new MassTransitOutboxBacklogSnapshot(
                    sample.PendingMessages,
                    sample.PendingMessages == 0 ? 0 : sample.OldestMessageAgeSeconds,
                    completedAt,
                    attemptedAt,
                    SampleSucceeded: true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var previous = Current;
            Interlocked.Exchange(
                ref _snapshot,
                previous with
                {
                    LastAttemptUtc = attemptedAt,
                    SampleSucceeded = false,
                });
            _logger.LogWarning(
                "Bounded MassTransit outbox sampling failed with {FailureType}; the prior sample is retained only for the configured grace window.",
                exception.GetType().Name);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.IntervalSeconds));

        await SampleOnceAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await SampleOnceAsync(stoppingToken);
    }
}
