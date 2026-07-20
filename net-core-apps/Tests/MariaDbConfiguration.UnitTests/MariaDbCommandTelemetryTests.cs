using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Inspacy.Data.MariaDb;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Xunit;

namespace MariaDbConfiguration.UnitTests;

public sealed class MariaDbCommandTelemetryTests
{
    private static readonly HashSet<string> AllowedOperations =
    [
        "select",
        "insert",
        "update",
        "delete",
        "call",
        "ddl",
        "other",
    ];

    [Fact]
    public void Registration_validates_options_and_reuses_one_thread_safe_interceptor()
    {
        var configuration = Configuration("250");
        var services = new ServiceCollection();

        services.AddMariaDbCommandTelemetry(configuration);
        services.AddMariaDbCommandTelemetry(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<MariaDbCommandTelemetryOptions>();
        var first = provider.GetRequiredService<MariaDbCommandTelemetryInterceptor>();
        var second = provider.GetRequiredService<MariaDbCommandTelemetryInterceptor>();

        Assert.Equal(TimeSpan.FromMilliseconds(250), options.SlowQueryThreshold);
        Assert.Same(first, second);
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(MariaDbCommandTelemetryInterceptor));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("60001")]
    [InlineData("2.5")]
    [InlineData("not-a-number")]
    public void Missing_or_invalid_threshold_fails_startup(string? configured)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            MariaDbCommandTelemetryOptions.Create(Configuration(configured)));

        Assert.Contains(
            MariaDbCommandTelemetryOptions.ConfigurationKey,
            error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(" SELECT * FROM anything", CommandType.Text, "select")]
    [InlineData("/* bounded comment */ WITH cte AS (SELECT 1) SELECT * FROM cte", CommandType.Text, "select")]
    [InlineData("-- comment\nINSERT INTO anything VALUES (1)", CommandType.Text, "insert")]
    [InlineData("REPLACE INTO anything VALUES (1)", CommandType.Text, "insert")]
    [InlineData("UPDATE anything SET value = 1", CommandType.Text, "update")]
    [InlineData("DELETE FROM anything", CommandType.Text, "delete")]
    [InlineData("CALL private_procedure()", CommandType.Text, "call")]
    [InlineData("CREATE TABLE anything (id INT)", CommandType.Text, "ddl")]
    [InlineData("GRANT SELECT ON anything TO somebody", CommandType.Text, "other")]
    [InlineData("private_procedure", CommandType.StoredProcedure, "call")]
    public void Operation_classifier_returns_only_the_bounded_vocabulary(
        string statement,
        CommandType commandType,
        string expected)
    {
        using var command = new MySqlCommand(statement) { CommandType = commandType };

        var operation = MariaDbCommandTelemetryInterceptor.ClassifyOperation(command);

        Assert.Equal(expected, operation);
        Assert.Contains(operation, AllowedOperations);
    }

    [Theory]
    [InlineData(1213, true, "deadlock")]
    [InlineData(1205, true, "lock_timeout")]
    [InlineData(1040, true, "transient")]
    [InlineData(1062, false, "non_transient")]
    public void Failure_classifier_uses_only_typed_connector_state(
        int errorCode,
        bool isTransient,
        string expected)
    {
        Assert.Equal(
            expected,
            MariaDbCommandTelemetryInterceptor.ClassifyMySqlFailure(errorCode, isTransient));
    }

    [Fact]
    public void Traces_and_metrics_emit_bounded_safe_tags_for_all_outcomes()
    {
        const string secretTable = "PrivateAccounts_938471";
        const string secretIdentifier = "user-71221";
        const string secretDatabase = "tenant_database_2281";
        const string secretPassword = "password-should-never-leak";
        const string secretFailure = "duplicate key for user-71221 in PrivateAccounts_938471";

        var activities = new ConcurrentQueue<ActivitySnapshot>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == MariaDbCommandTelemetryInterceptor.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(new ActivitySnapshot(
                activity.OperationName,
                activity.DisplayName,
                activity.Status,
                activity.Duration,
                activity.TagObjects.ToDictionary(
                    tag => tag.Key,
                    tag => tag.Value,
                    StringComparer.Ordinal))),
        };
        ActivitySource.AddActivityListener(activityListener);

        var measurements = new ConcurrentQueue<MeasurementSnapshot>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MariaDbCommandTelemetryInterceptor.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
            measurements.Enqueue(new MeasurementSnapshot(
                instrument.Name,
                measurement,
                Snapshot(tags))));
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            measurements.Enqueue(new MeasurementSnapshot(
                instrument.Name,
                measurement,
                Snapshot(tags))));
        meterListener.Start();

        var interceptor = new MariaDbCommandTelemetryInterceptor(
            new MariaDbCommandTelemetryOptions(10));
        using var connection = new MySqlConnection(
            $"Server=db.internal;Database={secretDatabase};User ID=private-user;Password={secretPassword}");
        using var select = new MySqlCommand(
            $"SELECT SecretId FROM {secretTable} WHERE UserId = @identifier",
            connection);
        select.Parameters.AddWithValue("@identifier", secretIdentifier);
        using var insert = new MySqlCommand(
            $"INSERT INTO {secretTable} (SecretId) VALUES (@identifier)",
            connection);
        insert.Parameters.AddWithValue("@identifier", secretIdentifier);
        using var delete = new MySqlCommand(
            $"DELETE FROM {secretTable} WHERE SecretId = @identifier",
            connection);
        delete.Parameters.AddWithValue("@identifier", secretIdentifier);

        Observe(interceptor, select, TimeSpan.FromMilliseconds(5), MariaDbCommandOutcome.Success);
        Observe(interceptor, insert, TimeSpan.FromMilliseconds(20), MariaDbCommandOutcome.Canceled);
        Observe(
            interceptor,
            delete,
            TimeSpan.FromMilliseconds(30),
            MariaDbCommandOutcome.Failure,
            secretFailure);

        Assert.Equal(3, activities.Count);
        Assert.All(activities, activity =>
        {
            Assert.Equal(MariaDbCommandTelemetryInterceptor.ActivityName, activity.OperationName);
            Assert.Equal(MariaDbCommandTelemetryInterceptor.ActivityName, activity.DisplayName);
            Assert.True(activity.Duration > TimeSpan.Zero);
            Assert.Equal(
                new[]
                {
                    "db.operation.name",
                    "db.system.name",
                    "inspacy.db.command.failure_kind",
                    "inspacy.db.command.outcome",
                    "inspacy.db.command.slow",
                },
                activity.Tags.Keys.Order(StringComparer.Ordinal));
            Assert.Contains((string)activity.Tags["db.operation.name"]!, AllowedOperations);
        });
        Assert.Single(activities, item => item.Status == ActivityStatusCode.Ok);
        Assert.Equal(2, activities.Count(item => item.Status == ActivityStatusCode.Error));
        Assert.Equal(
            new[] { "canceled", "failure", "success" },
            activities.Select(item => (string)item.Tags["inspacy.db.command.outcome"]!)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            new[] { "non_transient", "none", "none" },
            activities.Select(item => (string)item.Tags[MariaDbCommandTelemetryInterceptor.FailureKindTagName]!)
                .Order(StringComparer.Ordinal));

        var durations = measurements
            .Where(item => item.InstrumentName ==
                MariaDbCommandTelemetryInterceptor.DurationMetricName)
            .ToArray();
        Assert.Equal(new[] { 5d, 20d, 30d }, durations.Select(item => item.Value));
        Assert.Equal(3, measurements.Count(item => item.InstrumentName ==
            MariaDbCommandTelemetryInterceptor.ExecutionMetricName));
        Assert.Equal(2, measurements.Count(item => item.InstrumentName ==
            MariaDbCommandTelemetryInterceptor.SlowExecutionMetricName));
        Assert.All(measurements, measurement =>
        {
            Assert.Equal(
                new[]
                {
                    "db.operation.name",
                    "db.system.name",
                    "inspacy.db.command.failure_kind",
                    "inspacy.db.command.outcome",
                    "inspacy.db.command.slow",
                },
                measurement.Tags.Keys.Order(StringComparer.Ordinal));
            Assert.Equal("mariadb", measurement.Tags["db.system.name"]);
            Assert.Contains((string)measurement.Tags["db.operation.name"]!, AllowedOperations);
        });

        var exported = string.Join(
            '|',
            activities.SelectMany(ActivityValues)
                .Concat(measurements.SelectMany(MeasurementValues)));
        foreach (var sensitive in new[]
                 {
                     secretTable,
                     secretIdentifier,
                     secretDatabase,
                     secretPassword,
                     secretFailure,
                     select.CommandText,
                     insert.CommandText,
                     delete.CommandText,
                 })
        {
            Assert.DoesNotContain(sensitive, exported, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void Observe(
        MariaDbCommandTelemetryInterceptor interceptor,
        MySqlCommand command,
        TimeSpan duration,
        MariaDbCommandOutcome outcome,
        string? failureMessage = null)
    {
        var commandId = Guid.NewGuid();
        interceptor.Start(command, commandId);
        Thread.SpinWait(1_000);
        if (outcome == MariaDbCommandOutcome.Canceled)
        {
            interceptor.CommandCanceled(command, new CommandEndEventData(
                eventDefinition: null!,
                messageGenerator: static (_, _) => "sensitive event message must not be evaluated",
                command.Connection!,
                command,
                logCommandText: command.CommandText,
                context: null,
                DbCommandMethod.ExecuteNonQuery,
                commandId,
                Guid.NewGuid(),
                async: false,
                logParameterValues: true,
                DateTimeOffset.UtcNow - duration,
                duration,
                CommandSource.Unknown));
            return;
        }
        if (outcome == MariaDbCommandOutcome.Failure)
        {
            interceptor.CommandFailed(command, new CommandErrorEventData(
                eventDefinition: null!,
                messageGenerator: static (_, _) => "sensitive event message must not be evaluated",
                command.Connection!,
                command,
                logCommandText: command.CommandText,
                context: null,
                DbCommandMethod.ExecuteNonQuery,
                commandId,
                Guid.NewGuid(),
                new InvalidOperationException(failureMessage),
                async: false,
                logParameterValues: true,
                DateTimeOffset.UtcNow - duration,
                duration,
                CommandSource.Unknown));
            return;
        }

        interceptor.Complete(command, commandId, duration, outcome);
    }

    private static Dictionary<string, object?> Snapshot(
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var snapshot = new Dictionary<string, object?>(tags.Length, StringComparer.Ordinal);
        foreach (var tag in tags)
            snapshot[tag.Key] = tag.Value;
        return snapshot;
    }

    private static IEnumerable<string> ActivityValues(ActivitySnapshot activity) =>
        activity.Tags.SelectMany(tag => new[] { tag.Key, tag.Value?.ToString() ?? string.Empty })
            .Prepend(activity.DisplayName)
            .Prepend(activity.OperationName);

    private static IEnumerable<string> MeasurementValues(MeasurementSnapshot measurement) =>
        measurement.Tags.SelectMany(tag => new[] { tag.Key, tag.Value?.ToString() ?? string.Empty })
            .Prepend(measurement.InstrumentName);

    private static IConfigurationRoot Configuration(string? threshold) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [MariaDbCommandTelemetryOptions.ConfigurationKey] = threshold,
            })
            .Build();

    private sealed record ActivitySnapshot(
        string OperationName,
        string DisplayName,
        ActivityStatusCode Status,
        TimeSpan Duration,
        Dictionary<string, object?> Tags);

    private sealed record MeasurementSnapshot(
        string InstrumentName,
        double Value,
        Dictionary<string, object?> Tags);
}
