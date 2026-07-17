using Feed.Application.Dtos;
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
    public sealed class ChangeVisibilityConsumer : MessageBrokerRabbitMQ
    {
        private readonly ILogger<ChangeVisibilityConsumer> _logger;
        private readonly IServiceProvider _services;

        public ChangeVisibilityConsumer(
            IServiceProvider services,
            int retryCount,
            ILogger<ChangeVisibilityConsumer> logger)
            : base(
                services,
                Topology.Feed.EventsExchange,
                Topology.Feed.ChangeVisibility.RoutingKey,
                Topology.Feed.ChangeVisibility.Queues.Feed.ChangeVisibility,
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
                var payload = JsonConvert.DeserializeObject<ChangeVisibilityMessage>(message);
                using var scope = _services.CreateScope();
                var command = scope.ServiceProvider.GetRequiredService<ChangePostVisibilityCommand>();
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
