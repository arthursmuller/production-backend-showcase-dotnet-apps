using Comment.Domain.AggregatesModel;
using DomainEventing;
using StarterEntities;

namespace Comment.Domain.Events
{
    public record CommentAddedEvent(CommentPost Entity) : IDomainEvent;

    public record CommentUpdatedEvent(
        CommentPost Entity,
        VisibilityLevelRecord PreviousVisibilityLevel) : IDomainEvent;
}
