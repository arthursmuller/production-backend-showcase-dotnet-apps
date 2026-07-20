using MassTransit;
using MessageBrokerContracts.Privacy.Restricted;
using Moderation.Application.Features.Privacy.Commands;

namespace Moderation.API.AppInterfaces.Queues;

public sealed class UserErasureRequestedConsumer(
    ScheduleModerationUserErasureCommand command)
    : IConsumer<UserErasureRequested>
{
    public Task Consume(ConsumeContext<UserErasureRequested> context) =>
        command.Handle(context.Message, context.CancellationToken);
}

public sealed class UserErasureRequestedConsumerDefinition()
    : ModerationConsumerDefinition<UserErasureRequestedConsumer>(
        "moderation-user-erasure-requested");
