using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace MessageBrokerExtensions;

public sealed class ConsumeLoggingScopeFilter<T>(
    ILogger<ConsumeLoggingScopeFilter<T>> logger) : IFilter<ConsumeContext<T>>
    where T : class
{
    private static readonly string ContractType = typeof(T).FullName ?? typeof(T).Name;

    public void Probe(ProbeContext context) =>
        context.CreateFilterScope("inspacyConsumeLoggingScope");

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var activity = Activity.Current;
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["TraceId"] = activity?.TraceId.ToHexString(),
            ["SpanId"] = activity?.SpanId.ToHexString(),
            ["CorrelationId"] = context.CorrelationId?.ToString("D"),
            ["MessageId"] = context.MessageId?.ToString("D"),
            ["MessageContract"] = ContractType,
        });

        logger.LogDebug("Consuming integration message.");
        await next.Send(context);
        logger.LogDebug("Consumed integration message.");
    }
}
