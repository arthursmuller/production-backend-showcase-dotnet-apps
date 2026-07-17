using Comment.Application.Features.SocialInteractions.Commands;
using Comment.Application.MessageBrokers.Messages;
using MessageBrokerCore.Constants;
using MessageBrokerRabbitMq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace Comment.API.AppInterfaces.Queues
{
    public sealed class CommentCreatedConsumer : MessageBrokerRabbitMQ
    {
        private readonly ILogger<CommentCreatedConsumer> _logger;
        private readonly IServiceProvider _services;

        public CommentCreatedConsumer(
            IServiceProvider services,
            int retryCount,
            ILogger<CommentCreatedConsumer> logger)
            : base(
                services,
                Topology.Posts.EventsExchange,
                Topology.Comment.CommentCreated.RoutingKey,
                Topology.Comment.CommentCreated.Queues.Comment.CommentCreated,
                retryCount)
        {
            _logger = logger;
            _services = services;
            base.CreateConnection();
            base.RegisterQueue();
        }

        public override async Task ProcessMessageAsync(string message)
        {
            try
            {
                var payload = JsonConvert.DeserializeObject<CommentCreatedMessage>(message);
                using var scope = _services.CreateScope();
                var command = scope.ServiceProvider.GetRequiredService<UpdateCommentsCommand>();
                await command.Handle(payload.Dto);
            }
            catch (Exception exception)
            {
                LogFailure(exception, message);
                throw;
            }
        }

        private void LogFailure(Exception exception, string message)
        {
            var consumerName = GetType().Name;
            _logger.LogInformation(
                "{consumerName}: Message process failed, error:{error}, stackTrace:{stackTrace}, message:{message}",
                consumerName,
                exception.Message,
                exception.StackTrace,
                message);
            _logger.LogError(-1, exception, "{consumerName}: Message process failed", consumerName);
        }
    }
}
