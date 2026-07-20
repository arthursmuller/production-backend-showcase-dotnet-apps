using System;
using System.Threading;
using System.Threading.Tasks;
using MessageBrokerCore.Abstractions;

namespace InspacyApiHost;

/// <summary>
/// Fail-closed dependency-graph guard for the one-shot migration role. It deliberately
/// provides no transport and turns any unexpected publication attempt into a startup/job
/// failure instead of silently discarding an integration event.
/// </summary>
public sealed class MigrationRoleMessagePublisher : IIntegrationMessagePublisher
{
    public Task<Guid> PublishAsync<TMessage>(
        TMessage message,
        Guid messageId,
        CancellationToken cancellationToken)
        where TMessage : class =>
        throw new InvalidOperationException(
            "Message publication is disabled for the dedicated Migration runtime role.");
}
