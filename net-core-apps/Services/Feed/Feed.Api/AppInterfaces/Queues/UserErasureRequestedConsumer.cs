using Feed.Application.Features.Privacy;
using Feed.Infra.Persistence;
using MassTransit;
using MessageBrokerContracts.Privacy.Restricted;
using MessageBrokerExtensions;
using System;
using System.Threading.Tasks;

namespace Feed.API.AppInterfaces.Queues;

/// <summary>
/// Persists the permanent tombstone on the broker thread. High-cardinality cleanup is left to
/// the bounded, leased job worker.
/// </summary>
public sealed class UserErasureRequestedConsumer(
    IFeedUserErasureScheduler scheduler,
    TimeProvider timeProvider) : IConsumer<UserErasureRequested>
{
    public Task Consume(ConsumeContext<UserErasureRequested> context) =>
        scheduler.ScheduleAsync(
            context.Message,
            timeProvider.GetUtcNow().UtcDateTime,
            context.CancellationToken);
}

public sealed class UserErasureRequestedConsumerDefinition
    : EntityFrameworkOutboxConsumerDefinition<UserErasureRequestedConsumer, FeedContext>
{
    public UserErasureRequestedConsumerDefinition()
        : base(
            "feed_privacy_user_erasure",
            prefetchCount: 8,
            concurrencyLimit: 4)
    {
    }
}
