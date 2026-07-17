using Feed.Application.Features.Posts;
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
    public sealed class AddPostsConsumer : MessageBrokerRabbitMQ
    {
        private readonly ILogger<AddPostsConsumer> _logger;
        private readonly IServiceProvider _services;

        public AddPostsConsumer(
            IServiceProvider services,
            int retryCount,
            ILogger<AddPostsConsumer> logger)
            : base(
                services,
                Topology.Feed.EventsExchange,
                Topology.Feed.UserConnectionsAdd.RoutingKey,
                Topology.Feed.UserConnectionsAdd.Queues.Feed.UserConnectionsAdd,
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
                var payload = JsonConvert.DeserializeObject<AddPostsMessage>(message);
                using var scope = _services.CreateScope();
                var command = scope.ServiceProvider.GetRequiredService<AddPostCommand>();
                await command.Handle(payload.Dto);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "{Consumer} failed to process message {Message}", GetType().Name, message);
                throw;
            }
        }
    }
}
