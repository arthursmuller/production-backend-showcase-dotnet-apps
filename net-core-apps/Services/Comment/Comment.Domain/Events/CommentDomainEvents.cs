using Comment.Domain.AggregatesModel;
using DomainEventing;
using System;

namespace Comment.Domain.Events
{
    public record CommentPostCountChangedEvent(
        string RelatedEntityId,
        bool Increment,
        int UserId,
        string PostType) : IDomainEvent;

    public record CommentCategoryMentionsChangedEvent(
        string[] NamesToAdd,
        string[] NamesToRemove,
        int AuthorUserId) : IDomainEvent;

    public record CommentFeedAddRequestedEvent(
        CommentPost Comment,
        long SourceVersion,
        DateTime OccurredAtUtc) : IDomainEvent;

    public record CommentFeedVisibilityChangedEvent(
        int CommentId,
        string AudiencePolicy,
        int AuthorUserId,
        long SourceVersion,
        DateTime OccurredAtUtc) : IDomainEvent;

    public record CommentFeedRemoveRequestedEvent(
        int CommentId,
        int AuthorUserId,
        long SourceVersion,
        DateTime OccurredAtUtc) : IDomainEvent;

    public record CommentMentionNotificationRequestedEvent(
        CommentPost Comment,
        int RecipientUserId,
        string AudiencePolicy) : IDomainEvent;

    /// <summary>
    /// Canonical resource-state notification for the Interaction projection.
    /// The aggregate reference intentionally resolves the database-generated Id
    /// only when handlers run inside SaveEntitiesAsync.
    /// </summary>
    public record CommentInteractionStateChangedEvent(
        CommentPost Comment,
        bool IsNew,
        bool Deleted,
        long SourceVersion,
        DateTimeOffset OccurredAtUtc) : IDomainEvent;
}
