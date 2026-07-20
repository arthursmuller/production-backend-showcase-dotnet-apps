using MessageBrokerContracts.Following;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Application.Providers.Relationships
{
    public sealed record RelationshipTransition(
        int OwnerUserId,
        int ViewerUserId,
        string RelationshipKind,
        string State,
        string ReasonCode);

    /// <summary>Stages an event in Following's EF Bus Outbox without committing the unit of work.</summary>
    public interface IRelationshipEventWriter
    {
        Task<RelationshipStateChanged> StageAsync(
            RelationshipTransition transition,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Bounded authority-side bootstrap source. Callers must page asynchronously outside Feed's
    /// request path.
    /// </summary>
    public interface IRelationshipSnapshotReader
    {
        Task<RelationshipSnapshotPage> ReadPageAsync(
            long afterCursor,
            int pageSize,
            long? snapshotHighWatermark,
            CancellationToken cancellationToken);
    }

    public static class RelationshipSnapshotLimits
    {
        public const int MaximumPageSize = 500;
    }
}
