using MessageBrokerContracts.Following;
using System.Threading;
using System.Threading.Tasks;

namespace FollowingProvider.Abstractions
{
    public interface IFollowingApi
    {
        Task<RelationshipSnapshotPage> GetRelationshipSnapshotPageAsync(
            long afterCursor,
            int pageSize,
            long? snapshotHighWatermark,
            CancellationToken cancellationToken);
    }
}
