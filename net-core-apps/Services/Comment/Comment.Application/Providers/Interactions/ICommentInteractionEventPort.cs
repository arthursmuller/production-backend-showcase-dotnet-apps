using Interaction.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.Providers.Interactions;

public interface ICommentInteractionEventPort
{
    Task StageResourceStateAsync(
        CommentResourceStateChanged message,
        Guid messageId,
        CancellationToken cancellationToken);
    Task StageContributionAsync(
        ResourceContributionChanged message,
        Guid messageId,
        CancellationToken cancellationToken);
}
