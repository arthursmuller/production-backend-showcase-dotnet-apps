using System.Diagnostics;
using CommomStartupExtensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CommomStartupExtensions.UnitTests;

public sealed class JobExecutionScopeTests
{
    [Fact]
    public void Job_iteration_creates_one_bounded_activity_and_structured_scope()
    {
        Activity? started = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PlatformTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStarted = activity => started = activity,
        };
        ActivitySource.AddActivityListener(listener);
        var logger = new ScopeCapturingLogger();

        using (JobExecutionScope.Begin(logger, "feed-delivery"))
        {
            Assert.NotNull(started);
            Assert.Same(started, Activity.Current);
            Assert.Equal("job.execute", started!.OperationName);
            Assert.Equal("feed-delivery", started.GetTagItem("job.name"));
            Assert.Equal("feed-delivery", logger.Scope!["JobName"]);
            Assert.Equal(started.TraceId.ToString(), logger.Scope["TraceId"]);
            Assert.Equal(started.SpanId.ToString(), logger.Scope["SpanId"]);
            Assert.Equal(3, logger.Scope.Count);
        }

        Assert.Null(Activity.Current);
    }

    [Theory]
    [InlineData("contains user/42")]
    [InlineData("")]
    [InlineData("contains space")]
    public void Dynamic_or_unbounded_job_names_are_rejected(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            JobExecutionScope.Begin(new ScopeCapturingLogger(), value));
    }

    private sealed class ScopeCapturingLogger : ILogger
    {
        public IReadOnlyDictionary<string, object?>? Scope { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            Scope = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(state);
            return new ScopeLease();
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }

        private sealed class ScopeLease : IDisposable
        {
            public void Dispose() { }
        }
    }
}
