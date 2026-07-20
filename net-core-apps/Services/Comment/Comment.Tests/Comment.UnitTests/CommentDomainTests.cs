using Comment.Domain.AggregatesModel;
using Comment.Domain.Dtos;
using Comment.Domain.Exceptions;
using StarterEntities;
using StarterEntities.Storage;
using Xunit;
using Comment.Domain.Events;

namespace Comment.UnitTests;

public sealed class CommentDomainTests
{
    [Theory]
    [InlineData(1, "public")]
    [InlineData(2, "friends")]
    [InlineData(3, "friends_of_friends")]
    [InlineData(4, "owner")]
    [InlineData(999, "owner")]
    public void Visibility_ids_have_one_fail_closed_canonical_audience(int id, string expected) =>
        Assert.Equal(expected, CommentAudiencePolicies.FromVisibilityIdOrOwner(id));

    [Theory]
    [InlineData("global")]
    [InlineData("friendsoffriends")]
    [InlineData("friends-of-friends")]
    [InlineData("private")]
    public void Alternate_audience_spellings_fail_closed_to_owner(string value) =>
        Assert.Equal(CommentAudiencePolicies.Owner,
            CommentAudiencePolicies.NormalizeOrOwner(value));

    [Theory]
    [InlineData(2, "friends")]
    [InlineData(3, "friends_of_friends")]
    [InlineData(4, "owner")]
    public void Restricted_creation_never_publishes_public_aggregates(int visibilityId, string audience)
    {
        var comment = Create(visibilityId, withMention: true);

        Assert.Empty(comment.DomainEvents.OfType<CommentPostCountChangedEvent>());
        Assert.Empty(comment.DomainEvents.OfType<CommentCategoryMentionsChangedEvent>());
        var mention = Assert.Single(comment.DomainEvents.OfType<CommentMentionNotificationRequestedEvent>());
        Assert.Equal(audience, mention.AudiencePolicy);
        var feed = comment.DomainEvents.OfType<CommentFeedAddRequestedEvent>().SingleOrDefault();
        Assert.Equal(visibilityId == 4, feed is null);
        if (feed is not null)
            Assert.Equal(audience, feed.Comment.GetAudiencePolicy());
    }

    [Fact]
    public void Public_creation_is_the_only_visibility_counted_globally()
    {
        var comment = Create(1, withMention: false);
        Assert.Single(comment.DomainEvents.OfType<CommentPostCountChangedEvent>());
        Assert.Single(comment.DomainEvents.OfType<CommentCategoryMentionsChangedEvent>());
        Assert.Equal("public", comment.GetAudiencePolicy());
    }

    [Fact]
    public void Friends_to_friends_of_friends_emits_exact_visibility_change()
    {
        var comment = Create(2, withMention: false);
        comment.ClearEvents();
        comment.Update(17, new UpdateCommentDomainDto
        {
            Description = "updated",
            VisibilityLevelId = 3,
        });
        var changed = Assert.Single(comment.DomainEvents.OfType<CommentFeedVisibilityChangedEvent>());
        Assert.Equal("friends_of_friends", changed.AudiencePolicy);
        Assert.Empty(comment.DomainEvents.OfType<CommentPostCountChangedEvent>());
    }

    [Fact]
    public void Public_to_friends_removes_only_public_aggregates_and_keeps_restricted_feed_policy()
    {
        var comment = Create(1, withMention: false);
        comment.ClearEvents();
        comment.Update(17, new UpdateCommentDomainDto
        {
            Description = "restricted",
            VisibilityLevelId = 2,
        });

        Assert.False(Assert.Single(comment.DomainEvents.OfType<CommentPostCountChangedEvent>()).Increment);
        var categories = Assert.Single(comment.DomainEvents.OfType<CommentCategoryMentionsChangedEvent>());
        Assert.Empty(categories.NamesToAdd);
        Assert.Contains("Architecture", categories.NamesToRemove);
        Assert.Equal("friends", Assert.Single(comment.DomainEvents
            .OfType<CommentFeedVisibilityChangedEvent>()).AudiencePolicy);
    }

    [Fact]
    public void Friends_to_public_adds_public_aggregates_and_never_changes_via_boolean_approximation()
    {
        var comment = Create(2, withMention: false);
        comment.ClearEvents();
        comment.Update(17, new UpdateCommentDomainDto
        {
            Description = "public",
            VisibilityLevelId = 1,
        });

        Assert.True(Assert.Single(comment.DomainEvents.OfType<CommentPostCountChangedEvent>()).Increment);
        Assert.Contains("Architecture", Assert.Single(comment.DomainEvents
            .OfType<CommentCategoryMentionsChangedEvent>()).NamesToAdd);
        Assert.Equal("public", Assert.Single(comment.DomainEvents
            .OfType<CommentFeedVisibilityChangedEvent>()).AudiencePolicy);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Removing_restricted_comment_never_decrements_global_post_count(int visibilityId)
    {
        var comment = Create(visibilityId, withMention: false);
        comment.ClearEvents();
        comment.PrepareForRemoval(17);
        Assert.Empty(comment.DomainEvents.OfType<CommentPostCountChangedEvent>());
        Assert.Equal(visibilityId != 4,
            comment.DomainEvents.OfType<CommentFeedRemoveRequestedEvent>().Any());
    }

    [Fact]
    public void Create_normalizes_duplicate_category_names()
    {
        var comment = new CommentPost(
            new User(17, "Comment Author"),
            new AddCommentDomainDto
            {
                Description = "A comment about architecture",
                TargetId = "post-42",
                TargetType = "post",
                VisibilityLevelId = 1,
                Categories =
                [
                    new CategoryName("Architecture"),
                    new CategoryName(" architecture "),
                ],
            },
            Array.Empty<NewCommentMedia>());

        var category = Assert.Single(comment.Categories);
        Assert.Equal("Architecture", category.Name);
    }

    [Fact]
    public void Create_rejects_an_author_without_a_positive_identifier()
    {
        var action = () => new CommentPost(
            new User(0, "Invalid Author"),
            new AddCommentDomainDto
            {
                TargetId = "post-42",
                TargetType = "post",
            },
            Array.Empty<NewCommentMedia>());

        var exception = Assert.Throws<CommentException>(action);
        Assert.Equal("A valid comment author is required.", exception.Message);
    }

    [Fact]
    public void Create_normalizes_canonical_target_and_visibility_identity()
    {
        var comment = new CommentPost(
            new User(17, "Comment Author"),
            new AddCommentDomainDto
            {
                TargetId = " post-42 ",
                TargetType = " ARTICLE ",
                VisibilityLevelId = 1,
            },
            Array.Empty<NewCommentMedia>());

        Assert.Equal(17, comment.AuthorUserId);
        Assert.Equal("post-42", comment.TargetId);
        Assert.Equal("article", comment.TargetType);
        Assert.Equal(VisibilityLevel.Global.Id, comment.VisibilityLevelId);
        Assert.Equal(1, comment.Version);
    }

    private static CommentPost Create(int visibilityId, bool withMention) => new(
        new User(17, "Comment Author"),
        new AddCommentDomainDto
        {
            Description = withMention ? "hello @recipient" : "hello",
            TargetId = "post-42",
            TargetType = "post",
            VisibilityLevelId = visibilityId,
            Categories = [new CategoryName("Architecture")],
            Mentions = withMention ? [new UserMention(23, "recipient")] : [],
        },
        Array.Empty<NewCommentMedia>());
}
