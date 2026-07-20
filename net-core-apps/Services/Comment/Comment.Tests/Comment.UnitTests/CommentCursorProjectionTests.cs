using Comment.Application.Dtos;
using Comment.Application.Dtos.Interactions;
using Comment.Application.Features.Interactions;
using Comment.Domain.Exceptions;
using Comment.Domain.Repositories;
using Interaction.Contracts;
using StarterEntities.Storage;
using Xunit;

namespace Comment.UnitTests;

public sealed class CommentCursorProjectionTests
{
    [Fact]
    public void Cursor_boundary_graph_is_canonical_and_has_no_retired_engagement_fields()
    {
        Type[] boundaryTypes =
        [
            typeof(CommentCursorItemDto),
            typeof(CommentWriteResultDto),
            typeof(CommentCursorUserDto),
            typeof(CommentCursorVisibilityDto),
            typeof(CommentCursorEngagementDto),
            typeof(CommentCursorCategoryDto),
            typeof(CommentCursorMediaDto),
            typeof(CommentCursorMediaTypeDto),
            typeof(CommentCursorMentionDto),
            typeof(CommentCursorInteractionStatusDto),
        ];

        Assert.All(boundaryTypes, type =>
            Assert.Equal("Comment.Application.Dtos", type.Namespace));
        Assert.DoesNotContain(
            boundaryTypes.SelectMany(type => type.GetProperties()),
            property => string.Equals(
                property.Name,
                "TotalReports",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            boundaryTypes.SelectMany(type => type.GetProperties()),
            property => property.PropertyType.Namespace?.StartsWith(
                "Comment.Domain",
                StringComparison.Ordinal) == true);

        var itemProperties = typeof(CommentCursorItemDto).GetProperties()
            .Select(property => property.Name)
            .ToArray();
        Assert.Contains(nameof(CommentCursorItemDto.TargetId), itemProperties);
        Assert.Contains(nameof(CommentCursorItemDto.TargetType), itemProperties);
        Assert.Contains(nameof(CommentCursorItemDto.Engagement), itemProperties);
        Assert.Contains(nameof(CommentCursorItemDto.InteractionStatus), itemProperties);
        Assert.DoesNotContain("IdOfRelatedEntity", itemProperties);
        Assert.DoesNotContain("TypeOfRelatedEntity", itemProperties);
        Assert.DoesNotContain("Title", itemProperties);
        Assert.DoesNotContain("Social", itemProperties);
        Assert.DoesNotContain("SocialInteractionsStatus", itemProperties);

        var writeProperties = typeof(CommentWriteResultDto).GetProperties()
            .Select(property => property.Name)
            .ToArray();
        Assert.DoesNotContain("Engagement", writeProperties);
        Assert.DoesNotContain("InteractionStatus", writeProperties);

        var engagementProperties = typeof(CommentCursorEngagementDto).GetProperties()
            .Select(property => property.Name)
            .ToArray();
        Assert.DoesNotContain("Id", engagementProperties);
        Assert.DoesNotContain("CreatedDate", engagementProperties);
        Assert.DoesNotContain("UpdateDate", engagementProperties);
        Assert.DoesNotContain("UserUpdate", engagementProperties);
        Assert.DoesNotContain(
            "PostId",
            typeof(CommentCursorInteractionStatusDto).GetProperties()
                .Select(property => property.Name));
        Assert.Contains(
            "CommentId",
            typeof(CommentCursorInteractionStatusDto).GetProperties()
                .Select(property => property.Name));
    }

    [Fact]
    public void Cursor_read_model_maps_to_the_public_boundary_without_an_aggregate()
    {
        var created = new DateTime(2026, 7, 18, 1, 2, 3, DateTimeKind.Utc);
        var readModel = new CommentCursorReadModel(
            41,
            created,
            "projected body",
            "https://cdn.example/giphy.gif",
            "resource-9",
            "post",
            17,
            "Ada Lovelace",
            1,
            [new CommentCategoryCursorReadModel("architecture")],
            [new CommentMediaCursorReadModel(
                91,
                0,
                "https://cdn.example/image.png",
                "image.png",
                MediaType.Image,
                "https://cdn.example/thumb.png",
                null,
                "image/png",
                created)],
            [new CommentMentionCursorReadModel(23, "grace")]);

        var dto = CommentCursorDtoMapper.ToDto(readModel);

        Assert.Equal(readModel.Id, dto.Id);
        Assert.Equal(created, dto.CreatedDate);
        Assert.Equal("resource-9", dto.TargetId);
        Assert.Equal("post", dto.TargetType);
        Assert.Equal((17, "Ada Lovelace"), (dto.User.Id, dto.User.FullName));
        Assert.Equal((1, "global"), (dto.VisibilityLevel.Id, dto.VisibilityLevel.Name));
        Assert.Equal((0, 0, 0, 0, 0, 0, 0d, 0L),
            (dto.Engagement.TotalViews, dto.Engagement.TotalRatings,
                dto.Engagement.TotalLikes, dto.Engagement.TotalDislikes,
                dto.Engagement.TotalShares, dto.Engagement.TotalComments,
                dto.Engagement.RatingsTrueCount, dto.Engagement.Version));
        Assert.Equal("architecture", Assert.Single(dto.Categories).Name);
        Assert.Equal((91, 0, "image.png", CommentCursorMediaTypeDto.Image),
            (Assert.Single(dto.MediaItems).Id, dto.MediaItems[0].Order,
                dto.MediaItems[0].FileName, dto.MediaItems[0].MediaType));
        Assert.Equal("/api/comments/41/media/91", dto.MediaItems[0].Url);
        Assert.Null(dto.MediaItems[0].ThumbnailUrl);
        Assert.Equal((23, "grace"),
            (Assert.Single(dto.Mentions).UserId, dto.Mentions[0].UserName));
    }

    [Fact]
    public void Cursor_mapper_rejects_an_invalid_persisted_visibility_id()
    {
        var now = DateTime.UtcNow;
        var readModel = new CommentCursorReadModel(
            1,
            now,
            null!,
            null!,
            "resource",
            "post",
            2,
            "Owner",
            99,
            [],
            [],
            []);

        Assert.Throws<CommentException>(() => CommentCursorDtoMapper.ToDto(readModel));
    }

    [Fact]
    public async Task Interaction_projector_is_the_only_source_of_engagement_counters_and_version()
    {
        var now = DateTime.UtcNow;
        var dto = CommentCursorDtoMapper.ToDto(new CommentCursorReadModel(
            42,
            now,
            "body",
            null!,
            "resource",
            "post",
            7,
            "Owner",
            1,
            [],
            [],
            []));
        var summary = new CommentInteractionSummary(
            LikeCount: 5,
            DislikeCount: 3,
            RatingCount: 2,
            RatingTotal: 11,
            ViewCount: 8,
            CommentCount: 6,
            ShareCount: 4,
            Version: 17,
            HasGap: false);
        var projector = new CommentInteractionReadProjector(
            new StubProjectionStore(summary));

        await projector.ApplyToCursorDtosAsync([dto], CancellationToken.None);

        Assert.Equal((8, 2, 5, 3, 4, 6, 11d, 17L),
            (dto.Engagement.TotalViews, dto.Engagement.TotalRatings,
                dto.Engagement.TotalLikes, dto.Engagement.TotalDislikes,
                dto.Engagement.TotalShares, dto.Engagement.TotalComments,
                dto.Engagement.RatingsTrueCount, dto.Engagement.Version));
        Assert.Equal(5.5d, dto.Engagement.AverageRating);
    }

    private sealed class StubProjectionStore(CommentInteractionSummary summary)
        : ICommentInteractionProjectionStore
    {
        public Task ApplySummaryAsync(
            EngagementSummaryChanged message,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ApplyReactionAsync(
            ReactionChanged message,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ApplyRatingAsync(
            RatingChanged message,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<int, CommentInteractionSummary>> GetSummariesAsync(
            IReadOnlyCollection<int> commentIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<int, CommentInteractionSummary>>(
                commentIds.ToDictionary(commentId => commentId, _ => summary));
    }
}
