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
    public sealed class AddFeedItemsConsumer : MessageBrokerRabbitMQ
    {
        private readonly ILogger<AddFeedItemsConsumer> _logger;
        private readonly IServiceProvider _services;

        public AddFeedItemsConsumer(
            IServiceProvider services,
            int retryCount,
            ILogger<AddFeedItemsConsumer> logger)
            : base(
                services,
                Topology.Feed.EventsExchange,
                Topology.Feed.AddItems.RoutingKey,
                Topology.Feed.AddItems.Queues.Feed.AddItems,
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
                var payload = JsonConvert.DeserializeObject<AddFeedItemsMessage>(message);
                using var scope = _services.CreateScope();
                var command = scope.ServiceProvider.GetRequiredService<AddFeedItemsCommand>();
                await command.Handle(payload);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "{Consumer} failed to process message {Message}", GetType().Name, message);
                throw;
            }
        }
    }
}
