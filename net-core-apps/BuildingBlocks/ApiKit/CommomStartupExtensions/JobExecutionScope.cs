using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CommomStartupExtensions;

/// <summary>
/// Gives every bounded background-job iteration the same trace and structured-log boundary.
/// Job names are static code-owned values; execution, user, resource, and payload identifiers
/// are deliberately excluded.
/// </summary>
public sealed class JobExecutionScope : IDisposable
{
    private const int MaximumJobNameLength = 96;
    private readonly Activity? activity;
    private readonly IDisposable? loggingScope;

    private JobExecutionScope(Activity? activity, IDisposable? loggingScope)
    {
        this.activity = activity;
        this.loggingScope = loggingScope;
    }

    public static JobExecutionScope Begin(ILogger logger, string jobName)
    {
        ArgumentNullException.ThrowIfNull(logger);
        var normalized = Normalize(jobName);
        var activity = PlatformTelemetry.ActivitySource.StartActivity(
            "job.execute",
            ActivityKind.Internal);
        activity?.SetTag("job.name", normalized);

        var current = activity ?? Activity.Current;
        var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["TraceId"] = current?.TraceId.ToString(),
            ["SpanId"] = current?.SpanId.ToString(),
            ["JobName"] = normalized,
        });
        return new JobExecutionScope(activity, scope);
    }

    public void Dispose()
    {
        loggingScope?.Dispose();
        activity?.Dispose();
    }

    private static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > MaximumJobNameLength
            || normalized.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException(
                "Job names must be bounded static ASCII identifiers.",
                nameof(value));
        }

        return normalized;
    }
}
