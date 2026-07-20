using Comment.Application.Features.Privacy;
using Comment.Infra.Persistence;
using MassTransit;
using MessageBrokerContracts.Privacy.Restricted;
using MessageBrokerExtensions;
using System.Threading.Tasks;

namespace Comment.API.AppInterfaces.Queues;

/// <summary>The restricted broker thread persists the tombstone and only schedules work.</summary>
public sealed class UserErasureRequestedConsumer(
    ScheduleCommentUserErasureCommand command) : IConsumer<UserErasureRequested>
{
    public Task Consume(ConsumeContext<UserErasureRequested> context) =>
        command.Handle(context.Message, context.CancellationToken);
}

public sealed class UserErasureRequestedConsumerDefinition
    : EntityFrameworkOutboxConsumerDefinition<UserErasureRequestedConsumer, CommentContext>
{
    public UserErasureRequestedConsumerDefinition()
        : base(
            "comment_privacy_user_erasure",
            prefetchCount: 8,
            concurrencyLimit: 4)
    {
    }
}
