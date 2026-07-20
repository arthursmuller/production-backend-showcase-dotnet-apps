using Comment.Application.Features.Privacy;
using Comment.Infra.Persistence;
using MessageBrokerContracts.Privacy.Restricted;
using MessageBrokerCore.Abstractions;
using MessageBrokerExtensions;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Providers.Privacy;

public sealed class CommentPrivacyEventPublisher(
    IIntegrationMessagePublisher publisher,
    CommentContext context) : ICommentPrivacyEventPublisher
{
    public async Task StageCompletionAsync(
        UserErasureCompleted message,
        CancellationToken cancellationToken)
    {
        await context.PublishToBusOutboxAsync(
            publisher,
            message,
            message.EventId,
            cancellationToken);
    }
}
