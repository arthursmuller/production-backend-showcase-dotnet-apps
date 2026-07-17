using Following.Application.Features.Subscriptions;
using Following.Application.MessageBrokers.Messages;
using MessageBrokerCore.Constants;
using MessageBrokerRabbitMq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace Following.API.AppInterfaces.Queues
{
    public class FriendsSubscriptionFeatureUpdatedConsumer : MessageBrokerRabbitMQ
    {
        private readonly ILogger<FriendsSubscriptionFeatureUpdatedConsumer> _logger;
        private readonly IServiceProvider _services;

        public FriendsSubscriptionFeatureUpdatedConsumer(
            IServiceProvider services,
            int retryCount,
            ILogger<FriendsSubscriptionFeatureUpdatedConsumer> logger)
            : base(
                services,
                Topology.Posts.EventsExchange,
                Topology.Following.SubscriptionFeatureNetworking.RoutingKey,
                Topology.Following.SubscriptionFeatureNetworking.Queues.Following.FollowingQueue,
                retryCount)
        {
            _logger = logger;
            _services = services;
            CreateConnection();
            RegisterQueue();
        }

        public override async Task ProcessMessageAsync(string message)
        {
            try
            {
                var payload = JsonConvert.DeserializeObject<SubscriptionFeatureUpdatedMessage>(message);
                if (payload?.Dto is null)
                    return;

                using var scope = _services.CreateScope();
                var command = scope.ServiceProvider
                    .GetRequiredService<SetFriendsSubscriptionFeatureCommand>();
                await command.Handle(payload.Dto);
            }
            catch (Exception exception)
            {
                var consumerName = GetType().Name;
                _logger.LogInformation(
                    "{ConsumerName}: message processing failed. Error: {Message}; StackTrace: {StackTrace}; Payload: {Payload}",
                    consumerName,
                    exception.Message,
                    exception.StackTrace,
                    message);
                _logger.LogError(exception, "{ConsumerName}: message processing failed", consumerName);
                throw;
            }
        }
    }
}
