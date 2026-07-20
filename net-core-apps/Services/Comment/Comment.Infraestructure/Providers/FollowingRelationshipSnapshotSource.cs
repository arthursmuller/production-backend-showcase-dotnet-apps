using Comment.Application.Features.Visibility;
using FollowingProvider.Abstractions;
using MessageBrokerContracts.Following;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Providers
{
    /// <summary>Dedicated job-only Following snapshot adapter; never used on an HTTP request path.</summary>
    public sealed class FollowingRelationshipSnapshotSource(IFollowingService following)
        : ICommentRelationshipSnapshotSource
    {
        public Task<RelationshipSnapshotPage> ReadPageAsync(long afterCursor, int pageSize,
            long? snapshotHighWatermark, CancellationToken cancellationToken) =>
            following.Api.GetRelationshipSnapshotPageAsync(afterCursor, pageSize,
                snapshotHighWatermark, cancellationToken);
    }
}
