using Comment.Application.Dtos.Interactions;
using Interaction.Contracts;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.Features.Interactions;

public interface ICommentInteractionProjectionStore
{
    Task ApplySummaryAsync(EngagementSummaryChanged message, CancellationToken cancellationToken);
    Task ApplyReactionAsync(ReactionChanged message, CancellationToken cancellationToken);
    Task ApplyRatingAsync(RatingChanged message, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<int, CommentInteractionSummary>> GetSummariesAsync(
        IReadOnlyCollection<int> commentIds,
        CancellationToken cancellationToken);
}
