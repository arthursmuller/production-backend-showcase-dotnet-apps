using System.Collections.Concurrent;
using System.Diagnostics;
using MassTransit;
using MassTransit.Testing;
using MessageBrokerExtensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MassTransit.HarnessTests;

public sealed class ConsumeBoundaryHarnessTests
{
    [Fact]
    public async Task Built_in_instrumentation_propagates_activity_into_the_payload_free_consume_scope()
    {
        var scopeCapture = new ScopeCaptureLoggerProvider();
        var probe = new ConsumeProbe();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("MassTransit", StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(activityListener);

        await using var provider = ConfigureHarness<ScopeProbeConsumer>(scopeCapture, probe);
        var harness = await provider.StartTestHarness();
        var messageId = Guid.Parse("11223344-5566-7788-99aa-bbccddeeff00");
        var correlationId = Guid.Parse("aabbccdd-eeff-0011-2233-445566778899");
        using var parent = new Activity("harness.publish")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();

        await harness.Bus.Publish(
            new ScopeProbeMessage("payload-must-not-enter-scope"),
            context =>
            {
                context.MessageId = messageId;
                context.CorrelationId = correlationId;
            },
            TestContext.Current.CancellationToken);

        await probe.Entered.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);
        var scope = Assert.Single(scopeCapture.Scopes, item =>
            item.TryGetValue("MessageContract", out var contract)
            && string.Equals(
                contract?.ToString(),
                typeof(ScopeProbeMessage).FullName,
                StringComparison.Ordinal));

        Assert.Equal(parent.TraceId.ToHexString(), scope["TraceId"]);
        Assert.Matches("^[0-9a-f]{16}$", Assert.IsType<string>(scope["SpanId"]));
        Assert.Equal(correlationId.ToString("D"), scope["CorrelationId"]);
        Assert.Equal(messageId.ToString("D"), scope["MessageId"]);
        Assert.DoesNotContain(
            scope.Values.Select(value => value?.ToString()),
            value => value?.Contains("payload-must-not-enter-scope", StringComparison.Ordinal) == true);
    }

    private static ServiceProvider ConfigureHarness<TConsumer>(
        ScopeCaptureLoggerProvider scopeCapture,
        ConsumeProbe probe)
        where TConsumer : class, IConsumer
    {
        return new ServiceCollection()
            .AddSingleton(probe)
            .AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(scopeCapture);
                logging.SetMinimumLevel(LogLevel.Trace);
            })
            .AddScoped(typeof(ConsumeLoggingScopeFilter<>))
            .AddMassTransitTestHarness(registration =>
            {
                registration.SetTestTimeouts(
                    testTimeout: TimeSpan.FromSeconds(15),
                    testInactivityTimeout: TimeSpan.FromSeconds(2));
                registration.AddConsumer<TConsumer>();
                registration.UsingInMemory((context, bus) =>
                {
                    bus.UseInstrumentation(serviceName: "Inspacy.MassTransit.HarnessTests");
                    bus.UseConsumeFilter(typeof(ConsumeLoggingScopeFilter<>), context);
                    bus.ConfigureEndpoints(context);
                });
            })
            .BuildServiceProvider(validateScopes: true);
    }

    public sealed record ScopeProbeMessage(string SensitivePayload);

    public sealed class ScopeProbeConsumer(
        ConsumeProbe probe,
        ILogger<ScopeProbeConsumer> logger) : IConsumer<ScopeProbeMessage>
    {
        public Task Consume(ConsumeContext<ScopeProbeMessage> context)
        {
            logger.LogInformation("Probe message consumed.");
            probe.Entered.TrySetResult();
            return Task.CompletedTask;
        }
    }

    public sealed class ConsumeProbe
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ScopeCaptureLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<IReadOnlyDictionary<string, object?>> Scopes { get; } = [];

        public ILogger CreateLogger(string categoryName) => new ScopeCaptureLogger(Scopes);

        public void Dispose() { }

        private sealed class ScopeCaptureLogger(
            ConcurrentQueue<IReadOnlyDictionary<string, object?>> scopes) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                if (state is IEnumerable<KeyValuePair<string, object?>> values)
                    scopes.Enqueue(values.ToDictionary(item => item.Key, item => item.Value));
                return NullScope.Instance;
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
        }

        private sealed class NullScope : IDisposable
        {
            internal static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
