using Comment.Application.Providers.Interactions;
using Comment.Infra.Persistence;
using Interaction.Contracts;
using MassTransit;
using MessageBrokerExtensions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Providers.Interactions
{
    public sealed class CommentInteractionEventAdapter(
        IPublishEndpoint publisher,
        CommentContext context) : ICommentInteractionEventPort
    {
        public Task StageResourceStateAsync(
            CommentResourceStateChanged message,
            Guid messageId,
            CancellationToken cancellationToken) => StageAsync(message, messageId, cancellationToken);

        public Task StageContributionAsync(
            ResourceContributionChanged message,
            Guid messageId,
            CancellationToken cancellationToken) => StageAsync(message, messageId, cancellationToken);

        private async Task StageAsync<T>(T message, Guid messageId, CancellationToken cancellationToken)
            where T : class
        {
            if (messageId == Guid.Empty)
                throw new InvalidOperationException("Interaction messages require a stable non-empty ID.");

            await context.PublishToBusOutboxAsync(
                publisher,
                message,
                messageId,
                cancellationToken);
        }
    }
}
