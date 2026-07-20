using MessageBrokerContracts.Following;
using MessageBrokerContracts.Feed;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Application.Features.Audience
{
    public enum RelationshipProjectionOutcome
    {
        Applied,
        StaleOrDuplicate,
        GapQuarantined,
        InvalidQuarantined,
        Reconciled,
    }

    public sealed record RelationshipProjectionResult(
        RelationshipProjectionOutcome Outcome,
        Guid StreamId,
        long AppliedVersion,
        long ExpectedVersion);

    public interface IRelationshipProjectionWriter
    {
        Task<RelationshipProjectionResult> ApplyAsync(
            RelationshipStateChanged message,
            DateTime observedAtUtc,
            CancellationToken cancellationToken);
    }

    /// <summary>Applies bounded authority snapshots from a background reconciliation workflow.</summary>
    public interface IRelationshipProjectionReconciler
    {
        Task<int> ReconcilePageAsync(
            IReadOnlyCollection<RelationshipStateSnapshot> snapshots,
            DateTime observedAtUtc,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Async bootstrap boundary intentionally not used by Feed HTTP handlers. A job adapter may
    /// page the authority and pass each bounded page to IRelationshipProjectionReconciler.
    /// </summary>
    public interface IRelationshipBootstrapSource
    {
        Task<RelationshipSnapshotPage> ReadPageAsync(
            long afterCursor,
            int pageSize,
            long? snapshotHighWatermark,
            CancellationToken cancellationToken);
    }

    public sealed record RelationshipBootstrapPageResult(
        bool Acquired,
        bool HasMore,
        bool Completed,
        long NextCursor,
        string Outcome);

    public interface IRelationshipBootstrapProcessor
    {
        Task<RelationshipBootstrapPageResult> RunNextPageAsync(
            string workerId,
            DateTime observedAtUtc,
            CancellationToken cancellationToken);
    }

    public static class AudiencePolicies
    {
        public const string Public = FeedAudiencePolicies.Public;
        public const string Followers = FeedAudiencePolicies.Followers;
        public const string Friends = FeedAudiencePolicies.Friends;
        public const string FriendsOfFriends = FeedAudiencePolicies.FriendsOfFriends;
        public const string Owner = FeedAudiencePolicies.Owner;
    }

    public sealed record AudienceAccessDecision(bool Allowed, string ReasonCode)
    {
        public static AudienceAccessDecision Allow(string reason) => new(true, reason);
        public static AudienceAccessDecision Deny(string reason) => new(false, reason);
    }

    /// <summary>Reads only Feed's local projection and fails closed for required missing/stale state.</summary>
    public interface IAudienceProjectionReader
    {
        Task<AudienceAccessDecision> EvaluateAsync(
            int ownerUserId,
            int viewerUserId,
            string audiencePolicy,
            DateTime evaluatedAtUtc,
            TimeSpan maximumCheckpointAge,
            CancellationToken cancellationToken);
    }
}
