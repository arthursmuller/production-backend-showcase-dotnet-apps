using Interaction.Application.Features.Privacy.Commands;
using Interaction.Infraestructure.Database.Ef;
using MassTransit;
using MessageBrokerContracts.Privacy.Restricted;

namespace Interaction.Api.AppInterfaces.Queues;

public sealed class UserErasureRequestedConsumer(ScheduleUserErasureCommand command)
    : IConsumer<UserErasureRequested>
{
    public Task Consume(ConsumeContext<UserErasureRequested> context) =>
        command.Handle(context.Message, context.CancellationToken);
}

public sealed class UserErasureRequestedConsumerDefinition()
    : InteractionConsumerDefinition<UserErasureRequestedConsumer>(
        "interaction-user-erasure-requested");
