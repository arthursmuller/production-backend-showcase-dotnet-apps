using MessageBrokerContracts.Following;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.Features.Visibility
{
    public sealed class CommentAudienceProjectionOptions
    {
        public const string SectionName = "CommentAudienceProjection";

        // This is an activation/read gate only. Relationship propagation is never gated.
        public bool EnableReadEnforcement { get; set; }
        public int SnapshotPageSize { get; set; } = 500;
        public int MaximumTwoHopFirstHop { get; set; } = 256;
        public int MaximumCheckpointAgeSeconds { get; set; } = 86400;
        public int MaximumBootstrapAgeSeconds { get; set; } = 86400;
        public int BootstrapIntervalSeconds { get; set; } = 21600;
        public int BootstrapRetrySeconds { get; set; } = 15;
        public int BootstrapPageDelayMilliseconds { get; set; } = 50;
        public int BootstrapLeaseSeconds { get; set; } = 60;
        public int AuthorizationDeadlineMilliseconds { get; set; } = 750;
        public int ConsumerPrefetchCount { get; set; } = 64;
        public int ConsumerConcurrencyLimit { get; set; } = 32;

        public static bool IsValid(CommentAudienceProjectionOptions value) => value is not null
            && value.SnapshotPageSize is >= 1 and <= 500
            && value.MaximumTwoHopFirstHop is >= 1 and <= 500
            && value.MaximumCheckpointAgeSeconds is >= 60 and <= 604800
            && value.MaximumBootstrapAgeSeconds is >= 60 and <= 604800
            && value.BootstrapIntervalSeconds is >= 10 and <= 86400
            && value.BootstrapRetrySeconds is >= 1 and <= 3600
            && value.BootstrapPageDelayMilliseconds is >= 10 and <= 5000
            && value.BootstrapLeaseSeconds is >= 5 and <= 300
            && value.AuthorizationDeadlineMilliseconds is >= 50 and <= 2000
            && value.ConsumerPrefetchCount is >= 1 and <= 256
            && value.ConsumerConcurrencyLimit is >= 1 and <= 128;
    }

    public enum CommentAudienceOutcome
    {
        Allowed,
        Denied,
        ProjectionUnavailable,
    }

    public sealed record CommentAudienceDecision(CommentAudienceOutcome Outcome, string ReasonCode)
    {
        public bool Allowed => Outcome == CommentAudienceOutcome.Allowed;
        public static CommentAudienceDecision Allow(string reason) => new(CommentAudienceOutcome.Allowed, reason);
        public static CommentAudienceDecision Deny(string reason) => new(CommentAudienceOutcome.Denied, reason);
        public static CommentAudienceDecision Unavailable(string reason) => new(CommentAudienceOutcome.ProjectionUnavailable, reason);
    }

    /// <summary>Authorization reads Comment's local projection only; it never calls Following.</summary>
    public interface ICommentAudienceAuthorization
    {
        Task<CommentAudienceDecision> EvaluateCommentAsync(
            int commentId,
            int? viewerUserId,
            CancellationToken cancellationToken);

        Task<CommentAudienceDecision> EvaluateAudienceAsync(
            int ownerUserId,
            string audiencePolicy,
            int? viewerUserId,
            CancellationToken cancellationToken);

        Task<CommentAudienceDecision> EvaluateProjectionReadinessAsync(
            int? viewerUserId,
            CancellationToken cancellationToken);
    }

    public enum CommentRelationshipProjectionOutcome
    {
        Applied,
        StaleOrDuplicate,
        GapQuarantined,
        InvalidQuarantined,
        Reconciled,
        ErasedSuppressed,
    }

    public sealed record CommentRelationshipProjectionResult(
        CommentRelationshipProjectionOutcome Outcome,
        Guid StreamId,
        long AppliedVersion,
        long ExpectedVersion);

    public interface ICommentRelationshipProjectionWriter
    {
        Task<CommentRelationshipProjectionResult> ApplyAsync(
            RelationshipStateChanged message,
            DateTime observedAtUtc,
            CancellationToken cancellationToken);
    }

    public interface ICommentRelationshipProjectionReconciler
    {
        Task<int> ReconcilePageAsync(
            IReadOnlyCollection<RelationshipStateSnapshot> snapshots,
            DateTime observedAtUtc,
            CancellationToken cancellationToken);
    }

    /// <summary>Job-only authority boundary. HTTP request roles must not resolve this service.</summary>
    public interface ICommentRelationshipSnapshotSource
    {
        Task<RelationshipSnapshotPage> ReadPageAsync(
            long afterCursor,
            int pageSize,
            long? snapshotHighWatermark,
            CancellationToken cancellationToken);
    }

    public sealed record CommentRelationshipBootstrapPageResult(
        bool Acquired,
        bool HasMore,
        bool Completed,
        long NextCursor,
        string Outcome);

    public interface ICommentRelationshipBootstrapProcessor
    {
        Task<CommentRelationshipBootstrapPageResult> RunNextPageAsync(
            string workerId,
            DateTime observedAtUtc,
            CancellationToken cancellationToken);
    }
}
