using Feed.Domain.Exceptions;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Application.Features.Feeds
{
    public sealed record ListFeedQuery(
        IFeedReader reader,
        IIdentityService identity) : Query<FeedPageRequest, FeedPage>
    {
        public override Task<FeedPage> Handle(
            FeedPageRequest request,
            CancellationToken cancellationToken) =>
            reader.ReadAsync(identity.User.Id, request, cancellationToken);
    }

    public sealed record MarkFeedActivitiesSeenCommand(
        IFeedReader reader,
        IIdentityService identity,
        TimeProvider timeProvider) : Command<MarkFeedActivitiesSeen, bool>
    {
        public override async Task<bool> Handle(
            MarkFeedActivitiesSeen request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            var ids = request.ActivityIds?.Distinct().ToArray()
                ?? throw new FeedException("Feed activity identifiers are required.");
            if (ids.Length is < 1 or > 100 || ids.Any(id => id <= 0))
                throw new FeedException("Feed activity identifiers must contain 1-100 positive values.");
            await reader.AcknowledgeAsync(
                identity.User.Id,
                ids,
                timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);
            return true;
        }
    }
}
