using Feed.Application.Features.Feeds;
using Feed.Application.MessageBrokers.Messages;
using MessageBrokerCore.Constants;
using MessageBrokerRabbitMq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace Feed.API.AppInterfaces.Queues
{
    public sealed class RemoveFeedItemsConsumer : MessageBrokerRabbitMQ
    {
        private readonly ILogger<RemoveFeedItemsConsumer> _logger;
        private readonly IServiceProvider _services;

        public RemoveFeedItemsConsumer(
            IServiceProvider services,
            int retryCount,
            ILogger<RemoveFeedItemsConsumer> logger)
            : base(
                services,
                Topology.Feed.EventsExchange,
                Topology.Feed.RemoveItems.RoutingKey,
                Topology.Feed.RemoveItems.Queues.Feed.RemoveItems,
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
                var payload = JsonConvert.DeserializeObject<RemoveFeedItemsMessage>(message);
                using var scope = _services.CreateScope();
                var command = scope.ServiceProvider.GetRequiredService<RemoveFeedItemsCommand>();
                await command.Handle(payload.PostId);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "{Consumer} failed to process message {Message}", GetType().Name, message);
                throw;
            }
        }
    }
}
