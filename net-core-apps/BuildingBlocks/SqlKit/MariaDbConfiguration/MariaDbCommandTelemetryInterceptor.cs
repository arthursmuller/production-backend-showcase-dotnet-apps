using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Inspacy.Observability;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MySqlConnector;

namespace Inspacy.Data.MariaDb;

public sealed class MariaDbCommandTelemetryInterceptor(
    MariaDbCommandTelemetryOptions options) : DbCommandInterceptor
{
    public const string ActivitySourceName = PlatformInstrumentation.ActivitySourceName;
    public const string MeterName = PlatformInstrumentation.MeterName;
    public const string ActivityName = "mariadb.command";
    public const string DurationMetricName = "inspacy.mariadb.command.duration";
    public const string ExecutionMetricName = "inspacy.mariadb.command.executions";
    public const string SlowExecutionMetricName = "inspacy.mariadb.command.slow_executions";
    public const string FailureKindTagName = "inspacy.db.command.failure_kind";

    private const string DatabaseSystem = "mariadb";
    private const int MaximumClassificationCharacters = 512;

    private static readonly ActivitySource ActivitySource = PlatformInstrumentation.ActivitySource;
    private static readonly Meter Meter = PlatformInstrumentation.Meter;
    private static readonly Histogram<double> CommandDuration = Meter.CreateHistogram<double>(
        DurationMetricName,
        unit: "ms",
        description: "MariaDB command duration without statement or resource attributes.");
    private static readonly Counter<long> CommandExecutions = Meter.CreateCounter<long>(
        ExecutionMetricName,
        unit: "{command}",
        description: "MariaDB command outcomes grouped only by bounded safe attributes.");
    private static readonly Counter<long> SlowCommandExecutions = Meter.CreateCounter<long>(
        SlowExecutionMetricName,
        unit: "{command}",
        description: "MariaDB commands exceeding the configured duration threshold.");

    private readonly ConcurrentDictionary<Guid, CommandObservation> observations = new();

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Start(command, eventData.CommandId);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Start(command, eventData.CommandId);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Start(command, eventData.CommandId);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Start(command, eventData.CommandId);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Start(command, eventData.CommandId);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Start(command, eventData.CommandId);
        return ValueTask.FromResult(result);
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        Complete(command, eventData.CommandId, eventData.Duration, MariaDbCommandOutcome.Success);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Complete(command, eventData.CommandId, eventData.Duration, MariaDbCommandOutcome.Success);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        Complete(command, eventData.CommandId, eventData.Duration, MariaDbCommandOutcome.Success);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        Complete(command, eventData.CommandId, eventData.Duration, MariaDbCommandOutcome.Success);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        Complete(command, eventData.CommandId, eventData.Duration, MariaDbCommandOutcome.Success);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Complete(command, eventData.CommandId, eventData.Duration, MariaDbCommandOutcome.Success);
        return ValueTask.FromResult(result);
    }

    public override void CommandCanceled(DbCommand command, CommandEndEventData eventData) =>
        Complete(command, eventData.CommandId, eventData.Duration, MariaDbCommandOutcome.Canceled);

    public override Task CommandCanceledAsync(
        DbCommand command,
        CommandEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Complete(command, eventData.CommandId, eventData.Duration, MariaDbCommandOutcome.Canceled);
        return Task.CompletedTask;
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) =>
        Complete(
            command,
            eventData.CommandId,
            eventData.Duration,
            MariaDbCommandOutcome.Failure,
            eventData.Exception);

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Complete(
            command,
            eventData.CommandId,
            eventData.Duration,
            MariaDbCommandOutcome.Failure,
            eventData.Exception);
        return Task.CompletedTask;
    }

    internal void Start(DbCommand command, Guid commandId)
    {
        ArgumentNullException.ThrowIfNull(command);

        var operation = ClassifyOperation(command);
        var activity = ActivitySource.StartActivity(ActivityName, ActivityKind.Client);
        activity?.SetTag("db.system.name", DatabaseSystem);
        activity?.SetTag("db.operation.name", operation);

        var observation = new CommandObservation(operation, activity);
        if (observations.TryGetValue(commandId, out var replaced))
            replaced.Activity?.Dispose();
        observations[commandId] = observation;
    }

    internal void Complete(
        DbCommand command,
        Guid commandId,
        TimeSpan duration,
        MariaDbCommandOutcome outcome,
        Exception? exception = null)
    {
        ArgumentNullException.ThrowIfNull(command);

        observations.TryRemove(commandId, out var observation);
        var operation = observation?.Operation ?? ClassifyOperation(command);
        var safeDuration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        var isSlow = safeDuration >= options.SlowQueryThreshold;
        var outcomeName = OutcomeName(outcome);
        var failureKind = outcome is MariaDbCommandOutcome.Failure
            ? ClassifyFailure(exception)
            : "none";

        var tags = new TagList
        {
            { "db.system.name", DatabaseSystem },
            { "db.operation.name", operation },
            { "inspacy.db.command.outcome", outcomeName },
            { FailureKindTagName, failureKind },
            { "inspacy.db.command.slow", isSlow },
        };
        CommandDuration.Record(safeDuration.TotalMilliseconds, tags);
        CommandExecutions.Add(1, tags);
        if (isSlow)
            SlowCommandExecutions.Add(1, tags);

        var activity = observation?.Activity;
        if (activity is null)
            return;

        activity.SetTag("inspacy.db.command.outcome", outcomeName);
        activity.SetTag(FailureKindTagName, failureKind);
        activity.SetTag("inspacy.db.command.slow", isSlow);
        activity.SetStatus(
            outcome is MariaDbCommandOutcome.Success
                ? ActivityStatusCode.Ok
                : ActivityStatusCode.Error);
        activity.Dispose();
    }

    internal static string ClassifyOperation(DbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CommandType == CommandType.StoredProcedure)
            return "call";
        if (command.CommandType != CommandType.Text)
            return "other";

        var text = command.CommandText.AsSpan();
        if (text.Length > MaximumClassificationCharacters)
            text = text[..MaximumClassificationCharacters];
        text = SkipLeadingTrivia(text);

        var tokenLength = 0;
        while (tokenLength < text.Length && char.IsAsciiLetter(text[tokenLength]))
            tokenLength++;
        var token = text[..tokenLength];

        if (token.Equals("SELECT", StringComparison.OrdinalIgnoreCase)
            || token.Equals("WITH", StringComparison.OrdinalIgnoreCase))
        {
            return "select";
        }
        if (token.Equals("INSERT", StringComparison.OrdinalIgnoreCase)
            || token.Equals("REPLACE", StringComparison.OrdinalIgnoreCase))
        {
            return "insert";
        }
        if (token.Equals("UPDATE", StringComparison.OrdinalIgnoreCase))
            return "update";
        if (token.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
            return "delete";
        if (token.Equals("CALL", StringComparison.OrdinalIgnoreCase))
            return "call";
        if (token.Equals("CREATE", StringComparison.OrdinalIgnoreCase)
            || token.Equals("ALTER", StringComparison.OrdinalIgnoreCase)
            || token.Equals("DROP", StringComparison.OrdinalIgnoreCase)
            || token.Equals("TRUNCATE", StringComparison.OrdinalIgnoreCase)
            || token.Equals("RENAME", StringComparison.OrdinalIgnoreCase)
            || token.Equals("ANALYZE", StringComparison.OrdinalIgnoreCase)
            || token.Equals("OPTIMIZE", StringComparison.OrdinalIgnoreCase))
        {
            return "ddl";
        }

        return "other";
    }

    private static ReadOnlySpan<char> SkipLeadingTrivia(ReadOnlySpan<char> text)
    {
        while (!text.IsEmpty)
        {
            while (!text.IsEmpty && (char.IsWhiteSpace(text[0]) || text[0] == ';'))
                text = text[1..];

            if (text.StartsWith("--", StringComparison.Ordinal))
            {
                var newline = text.IndexOf('\n');
                text = newline < 0 ? [] : text[(newline + 1)..];
                continue;
            }
            if (text.StartsWith("#", StringComparison.Ordinal))
            {
                var newline = text.IndexOf('\n');
                text = newline < 0 ? [] : text[(newline + 1)..];
                continue;
            }
            if (text.StartsWith("/*", StringComparison.Ordinal))
            {
                var close = text.IndexOf("*/", StringComparison.Ordinal);
                text = close < 0 ? [] : text[(close + 2)..];
                continue;
            }

            break;
        }

        return text;
    }

    private static string OutcomeName(MariaDbCommandOutcome outcome) => outcome switch
    {
        MariaDbCommandOutcome.Success => "success",
        MariaDbCommandOutcome.Canceled => "canceled",
        MariaDbCommandOutcome.Failure => "failure",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    internal static string ClassifyFailure(Exception? exception)
    {
        for (var depth = 0; exception is not null && depth < 4; depth++)
        {
            if (exception is MySqlException mysqlException)
            {
                return ClassifyMySqlFailure(
                    mysqlException.Number,
                    mysqlException.IsTransient);
            }
            exception = exception.InnerException;
        }

        return "non_transient";
    }

    internal static string ClassifyMySqlFailure(int errorCode, bool isTransient) =>
        errorCode switch
        {
            1213 => "deadlock",
            1205 => "lock_timeout",
            _ when isTransient => "transient",
            _ => "non_transient",
        };

    private sealed record CommandObservation(string Operation, Activity? Activity);
}

internal enum MariaDbCommandOutcome
{
    Success,
    Canceled,
    Failure,
}
