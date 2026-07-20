using Comment.Application.Features.Visibility;
using Comment.Domain.AggregatesModel;
using Comment.Domain.AggregatesModel.Visibility;
using MessageBrokerContracts.Following;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Persistence.Visibility
{
    /// <summary>
    /// Set-based, Comment-local audience policy. Missing, stale, gapped, or unbootstrapped
    /// relationship knowledge is never interpreted as permission.
    /// </summary>
    public sealed class CommentAudienceSqlPolicy(
        CommentContext context,
        IOptions<CommentAudienceProjectionOptions> options,
        CommentAudienceProjectionTelemetry telemetry,
        TimeProvider timeProvider) : ICommentAudienceAuthorization
    {
        private readonly CommentAudienceProjectionOptions _options = options.Value;

        public async Task<CommentAudienceDecision> EvaluateCommentAsync(
            int commentId, int? viewerUserId, CancellationToken cancellationToken)
        {
            if (commentId <= 0)
                return CommentAudienceDecision.Deny("invalid_comment");
            var metadata = await context.Comments.IgnoreQueryFilters().AsNoTracking()
                .Where(item => item.Id == commentId)
                .Select(item => new { item.AuthorUserId, item.VisibilityLevelId })
                .SingleOrDefaultAsync(cancellationToken);
            if (metadata is null)
                return CommentAudienceDecision.Deny("missing_comment");
            return await EvaluateAudienceAsync(metadata.AuthorUserId,
                CommentAudiencePolicies.FromVisibilityIdOrOwner(metadata.VisibilityLevelId),
                viewerUserId, cancellationToken);
        }

        public async Task<CommentAudienceDecision> EvaluateAudienceAsync(
            int ownerUserId, string audiencePolicy, int? viewerUserId,
            CancellationToken cancellationToken)
        {
            var audience = CommentAudiencePolicies.NormalizeOrOwner(audiencePolicy);
            if (ownerUserId <= 0)
                return Record(audience, CommentAudienceDecision.Deny("invalid_owner"));
            if (viewerUserId == ownerUserId)
                return Record(audience, CommentAudienceDecision.Allow("owner"));
            if (!viewerUserId.HasValue || viewerUserId <= 0)
                return Record(audience, audience == CommentAudiencePolicies.Public
                    ? CommentAudienceDecision.Allow("anonymous_public")
                    : CommentAudienceDecision.Deny("anonymous_restricted"));
            if (audience == CommentAudiencePolicies.Owner)
                return Record(audience, CommentAudienceDecision.Deny("owner_only"));

            var readiness = await EvaluateProjectionReadinessAsync(viewerUserId, cancellationToken);
            if (readiness.Outcome != CommentAudienceOutcome.Allowed)
                return Record(audience, readiness);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMilliseconds(_options.AuthorizationDeadlineMilliseconds));
            try
            {
                var viewer = viewerUserId.Value;
                var cutoff = Now() - TimeSpan.FromSeconds(_options.MaximumCheckpointAgeSeconds);
                if (await HasHealthyBlockAsync(ownerUserId, viewer, cutoff, deadline.Token))
                    return Record(audience, CommentAudienceDecision.Deny("blocked"));
                if (audience == CommentAudiencePolicies.Public)
                    return Record(audience, CommentAudienceDecision.Allow("public"));
                if (await HealthyFriendEdges(cutoff).AnyAsync(edge =>
                    edge.OwnerUserId == ownerUserId && edge.ViewerUserId == viewer, deadline.Token))
                    return Record(audience, CommentAudienceDecision.Allow("direct_friend"));
                if (audience != CommentAudiencePolicies.FriendsOfFriends)
                    return Record(audience, CommentAudienceDecision.Deny("not_friend"));

                var intermediateIds = await HealthyFriendEdges(cutoff)
                    .Where(edge => edge.OwnerUserId == ownerUserId && edge.ViewerUserId != viewer
                        && !HealthyBlockEdges(cutoff).Any(block =>
                            (block.OwnerUserId == ownerUserId && block.ViewerUserId == edge.ViewerUserId)
                            || (block.OwnerUserId == edge.ViewerUserId && block.ViewerUserId == ownerUserId)))
                    .OrderBy(edge => edge.ViewerUserId)
                    .Select(edge => edge.ViewerUserId)
                    .Distinct()
                    .Take(_options.MaximumTwoHopFirstHop)
                    .ToArrayAsync(deadline.Token);
                if (intermediateIds.Length == 0)
                    return Record(audience, CommentAudienceDecision.Deny("no_two_hop_path"));
                var hasTwoHop = await HealthyFriendEdges(cutoff).AnyAsync(edge =>
                    intermediateIds.Contains(edge.OwnerUserId) && edge.ViewerUserId == viewer
                    && !HealthyBlockEdges(cutoff).Any(block =>
                        (block.OwnerUserId == edge.OwnerUserId && block.ViewerUserId == viewer)
                        || (block.OwnerUserId == viewer && block.ViewerUserId == edge.OwnerUserId)),
                    deadline.Token);
                return Record(audience, hasTwoHop
                    ? CommentAudienceDecision.Allow("two_hop_friend")
                    : CommentAudienceDecision.Deny("no_two_hop_path"));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Record(audience, CommentAudienceDecision.Unavailable("authorization_budget_exceeded"));
            }
        }

        public async Task<CommentAudienceDecision> EvaluateProjectionReadinessAsync(
            int? viewerUserId, CancellationToken cancellationToken)
        {
            if (!viewerUserId.HasValue || viewerUserId <= 0)
                return CommentAudienceDecision.Allow("anonymous");
            if (!_options.EnableReadEnforcement)
                return CommentAudienceDecision.Unavailable("read_gate_disabled");
            var now = Now();
            var state = await context.CommentAudienceProjectionStates.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == CommentAudienceProjectionState.SingletonId,
                    cancellationToken);
            if (state is null || !state.IsCompleteAndFresh(now,
                TimeSpan.FromSeconds(_options.MaximumBootstrapAgeSeconds)))
                return CommentAudienceDecision.Unavailable(state is null
                    ? "bootstrap_missing" : "bootstrap_incomplete_or_stale");
            var hasUnhealthy = await context.CommentRelationshipCheckpoints.AsNoTracking()
                .AnyAsync(item => item.Status != CommentProjectionStates.Healthy, cancellationToken);
            if (hasUnhealthy)
                return CommentAudienceDecision.Unavailable("checkpoint_unhealthy");
            var unresolved = await context.CommentRelationshipEventQuarantines.AsNoTracking()
                .AnyAsync(item => item.ResolvedAtUtc == null, cancellationToken);
            return unresolved
                ? CommentAudienceDecision.Unavailable("quarantine_unresolved")
                : CommentAudienceDecision.Allow("projection_ready");
        }

        public async Task<IQueryable<CommentPost>> ApplyReadFilterAsync(
            IQueryable<CommentPost> source,
            int? viewerUserId,
            CancellationToken cancellationToken)
        {
            source = source.IgnoreQueryFilters();
            if (!viewerUserId.HasValue || viewerUserId <= 0)
                return source.Where(comment => comment.VisibilityLevelId == 1);

            var ready = await EvaluateProjectionReadinessAsync(viewerUserId, cancellationToken);
            if (ready.Outcome != CommentAudienceOutcome.Allowed)
                throw new Comment.Domain.Exceptions.CommentException(
                    "Comment audience projection is not ready.", 503);

            var viewer = viewerUserId.Value;
            var cutoff = Now() - TimeSpan.FromSeconds(_options.MaximumCheckpointAgeSeconds);
            var friendEdges = HealthyFriendEdges(cutoff);
            var blockEdges = HealthyBlockEdges(cutoff);
            var intermediateIds = await friendEdges
                .Where(edge => edge.ViewerUserId == viewer
                    && !blockEdges.Any(block =>
                        (block.OwnerUserId == edge.OwnerUserId && block.ViewerUserId == viewer)
                        || (block.OwnerUserId == viewer && block.ViewerUserId == edge.OwnerUserId)))
                .OrderBy(edge => edge.OwnerUserId)
                .Select(edge => edge.OwnerUserId)
                .Distinct()
                .Take(_options.MaximumTwoHopFirstHop)
                .ToArrayAsync(cancellationToken);
            var twoHopOwnerIds = intermediateIds.Length == 0
                ? Array.Empty<int>()
                : await friendEdges
                    .Where(edge => intermediateIds.Contains(edge.ViewerUserId)
                        && !blockEdges.Any(block =>
                            (block.OwnerUserId == edge.OwnerUserId
                                && block.ViewerUserId == edge.ViewerUserId)
                            || (block.OwnerUserId == edge.ViewerUserId
                                && block.ViewerUserId == edge.OwnerUserId)))
                    .OrderBy(edge => edge.OwnerUserId)
                    .Select(edge => edge.OwnerUserId)
                    .Distinct()
                    .Take(_options.MaximumTwoHopFirstHop)
                    .ToArrayAsync(cancellationToken);

            return source.Where(comment =>
                comment.AuthorUserId == viewer
                || (!blockEdges.Any(block =>
                        (block.OwnerUserId == comment.AuthorUserId && block.ViewerUserId == viewer)
                        || (block.OwnerUserId == viewer && block.ViewerUserId == comment.AuthorUserId))
                    && (comment.VisibilityLevelId == 1
                        || (comment.VisibilityLevelId == 2 && friendEdges.Any(friend =>
                            friend.OwnerUserId == comment.AuthorUserId && friend.ViewerUserId == viewer))
                        || (comment.VisibilityLevelId == 3
                            && (friendEdges.Any(friend => friend.OwnerUserId == comment.AuthorUserId
                                    && friend.ViewerUserId == viewer)
                                || twoHopOwnerIds.Contains(comment.AuthorUserId))))));
        }

        private IQueryable<CommentRelationshipEdge> HealthyFriendEdges(DateTime cutoff) =>
            HealthyEdges(cutoff).Where(edge => edge.RelationshipKind == RelationshipKinds.Friend
                && edge.State == RelationshipStates.Active);

        private IQueryable<CommentRelationshipEdge> HealthyBlockEdges(DateTime cutoff) =>
            HealthyEdges(cutoff).Where(edge => edge.RelationshipKind == RelationshipKinds.Block
                && edge.State == RelationshipStates.Blocked);

        private IQueryable<CommentRelationshipEdge> HealthyEdges(DateTime cutoff) =>
            context.CommentRelationshipEdges.AsNoTracking()
                .Join(context.CommentRelationshipCheckpoints.AsNoTracking().Where(checkpoint =>
                        checkpoint.Status == CommentProjectionStates.Healthy
                        && checkpoint.LastConfirmedAtUtc >= cutoff),
                    edge => edge.SourceStreamId,
                    checkpoint => checkpoint.StreamId,
                    (edge, checkpoint) => new { edge, checkpoint })
                .Where(pair => pair.edge.SourceVersion == pair.checkpoint.LastAppliedVersion)
                .Select(pair => pair.edge);

        private Task<bool> HasHealthyBlockAsync(int owner, int viewer, DateTime cutoff,
            CancellationToken cancellationToken) => HealthyBlockEdges(cutoff).AnyAsync(block =>
                (block.OwnerUserId == owner && block.ViewerUserId == viewer)
                || (block.OwnerUserId == viewer && block.ViewerUserId == owner), cancellationToken);

        private CommentAudienceDecision Record(string audience, CommentAudienceDecision decision)
        {
            telemetry.Authorization(audience, decision.Outcome switch
            {
                CommentAudienceOutcome.Allowed => "allowed",
                CommentAudienceOutcome.Denied => "denied",
                _ => "unavailable",
            });
            return decision;
        }

        private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
    }
}
