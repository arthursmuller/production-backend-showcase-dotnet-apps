using Following.Application.Dtos;
using Following.Application.Features.Subscriptions;
using Following.Infra.Persistence;
using Following.Domain.Exceptions;
using MassTransit;
using MessageBrokerContracts.Following;
using MessageBrokerCore.Constants;
using MessageBrokerExtensions;
using System;
using System.Threading.Tasks;

namespace Following.API.AppInterfaces.Queues
{
    public sealed class FriendsSubscriptionFeatureUpdatedConsumer(
        SetFriendsSubscriptionFeatureCommand command)
        : IConsumer<SubscriptionFeatureUpdated>
    {
        public async Task Consume(ConsumeContext<SubscriptionFeatureUpdated> context)
        {
            try
            {
                await command.Handle(Map(context.Message), context.CancellationToken);
            }
            catch (FollowingException exception) when (exception.StatusCode == 410)
            {
                // A durable privacy tombstone is a terminal rejection for delayed subscription
                // replays; acknowledge it instead of building a permanent broker poison loop.
            }
        }

        internal static SubscriptionFeatureUpdatedDto Map(
            SubscriptionFeatureUpdated message)
        {
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(message.Owner);
            ArgumentNullException.ThrowIfNull(message.Requester);
            if (message.EventId == Guid.Empty
                || message.OccurredAtUtc.Kind != DateTimeKind.Utc
                || message.EndDateUtc is { Kind: not DateTimeKind.Utc })
            {
                throw new InvalidOperationException(
                    "The subscription feature update envelope is invalid.");
            }

            return new SubscriptionFeatureUpdatedDto(
                new SubscriptionFeatureUpdatedUserDto(
                    message.Owner.UserId,
                    message.Owner.DisplayName),
                new SubscriptionFeatureUpdatedUserDto(
                    message.Requester.UserId,
                    message.Requester.DisplayName),
                message.SubscriptionId,
                message.EndDateUtc);
        }
    }

    public sealed class FriendsSubscriptionFeatureUpdatedConsumerDefinition
        : EntityFrameworkOutboxConsumerDefinition<FriendsSubscriptionFeatureUpdatedConsumer, FollowingContext>
    {
        public FriendsSubscriptionFeatureUpdatedConsumerDefinition()
            : base(Topology.Following.SubscriptionFeatureNetworking.Queue)
        {
        }
    }
}
