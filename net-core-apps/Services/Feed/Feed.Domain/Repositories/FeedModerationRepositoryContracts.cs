using Feed.Domain.AggregatesModel.Moderation;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Domain.Repositories;

public interface IFeedModerationDecisionRepository
{
    Task ApplyAsync(
        FeedModerationDecisionInput decision,
        CancellationToken cancellationToken);
}
