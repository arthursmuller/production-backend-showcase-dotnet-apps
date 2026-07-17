using Comment.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comment.Infra.EntityConfigurations
{
    public class CommentConfiguration : BaseConfiguration, IEntityTypeConfiguration<CommentPost>
    {
        public void Configure(EntityTypeBuilder<CommentPost> builder)
        {
            setPropriedadesDeEntidadeBase(builder);
            builder.Ignore(e => e.DomainEvents);

            builder.Property(m => m.Description)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.GifUrl)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.IdOfRelatedEntity)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.TypeOfRelatedEntity)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.PublishedOnFeed)
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            builder.Property(b => b.RelatedEntity)
                .HasComputedColumnSql("LOWER(CONCAT(IdOfRelatedEntity,TypeOfRelatedEntity))");

            builder.HasOne(e => e.Social);
            builder.OwnsOne(e => e.User);
            builder.OwnsOne(e => e.VisibilityLevel);
            builder.OwnsMany(e => e.Categories);
            builder.OwnsMany(e => e.Mentions, mb => mb.ToTable("CommentMentions"));
            builder.Navigation(e => e.Mentions).AutoInclude();
            builder.HasMany(e => e.MediaItems);
            builder.Navigation(e => e.MediaItems).AutoInclude();

            builder.Navigation(e => e.Social).AutoInclude(true);
        }
    }

    public class CommentMediaItemEntityConfiguration : BaseConfiguration, IEntityTypeConfiguration<CommentMediaItem>
    {
        public void Configure(EntityTypeBuilder<CommentMediaItem> builder)
        {
            setPropriedadesDeEntidadeBase(builder);
            builder.Property(x => x.Order).IsRequired();
            builder.Property(x => x.Url).IsRequired().HasMaxLength(500);
            builder.Property(x => x.FileName).IsRequired().HasMaxLength(255);
            builder.Property(x => x.MediaType).IsRequired().HasConversion<int>();
            builder.Property(x => x.ThumbnailUrl).HasMaxLength(500);
            builder.Property(x => x.DurationSeconds);
            builder.Property(x => x.MimeType).HasMaxLength(100);
        }
    }

    #region SOCIAL
    public class CommentSocialAnalyticsConfiguration : StarterConfiguration, IEntityTypeConfiguration<CommentSocialAnalytics>
    {
        public void Configure(EntityTypeBuilder<CommentSocialAnalytics> builder)
        {
            setRatingAnalyticsProps<CommentSocialAnalytics, ValueGeneratorCommentAverageRating>(builder);
        }
    }
    public class CommentRatingConfiguration : StarterConfiguration, IEntityTypeConfiguration<CommentSocialRating>
    {
        public void Configure(EntityTypeBuilder<CommentSocialRating> builder)
        {
            setRatingProps<
                CommentSocialRating,
                CommentLike,
                CommentDislike,
                CommentRatingLevel,
                CommentReport,
                CommentShare,
                CommentPostComment
            >(builder);

            builder.Property(m => m.CommentId)
              .UsePropertyAccessMode(PropertyAccessMode.Field);

            builder.HasOne(e => e.Analytics);
            builder.Navigation(e => e.Analytics).AutoInclude(true);
        }
    }
    public class CommentLikeConfiguration : SocialInteractionsConfiguration, IEntityTypeConfiguration<CommentLike>
    {
        public void Configure(EntityTypeBuilder<CommentLike> builder)
        {
            setCommomLike(builder);
        }
    }
    public class CommentDislikeConfiguration : SocialInteractionsConfiguration, IEntityTypeConfiguration<CommentDislike>
    {
        public void Configure(EntityTypeBuilder<CommentDislike> builder)
        {
            setCommomDislike(builder);
        }
    }
    public class CommentRatingLevelConfiguration : SocialInteractionsConfiguration, IEntityTypeConfiguration<CommentRatingLevel>
    {
        public void Configure(EntityTypeBuilder<CommentRatingLevel> builder)
        {
            setCommomRatingLevel(builder);
        }
    }
    public class CommentReportConfiguration : SocialInteractionsConfiguration, IEntityTypeConfiguration<CommentReport>
    {
        public void Configure(EntityTypeBuilder<CommentReport> builder)
        {
            setCommomReport(builder);
        }
    }
    public class CommentShareConfiguration : SocialInteractionsConfiguration, IEntityTypeConfiguration<CommentShare>
    {
        public void Configure(EntityTypeBuilder<CommentShare> builder)
        {
            setCommomShare(builder);
        }
    }
    public class CommentCommentConfiguration : SocialInteractionsConfiguration, IEntityTypeConfiguration<CommentPostComment>
    {
        public void Configure(EntityTypeBuilder<CommentPostComment> builder)
        {
            setCommomComment(builder);
        }
    }
    public class CommentBookmarkConfiguration : SocialInteractionsConfiguration, IEntityTypeConfiguration<CommentBookmark>
    {
        public void Configure(EntityTypeBuilder<CommentBookmark> builder)
        {
            setPropriedadesDeEntidadeBase(builder);

            builder.Property(m => m.UserId).UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.PostId).UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.GroupId).UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.GroupName).HasMaxLength(120).UsePropertyAccessMode(PropertyAccessMode.Field);

            builder.HasIndex(m => new { m.UserId, m.PostId });
            builder.HasIndex(m => new { m.UserId, m.GroupId, m.PostId }).IsUnique();
        }
    }

    #endregion

    #region TRANSFORMERS
    class ValueGeneratorCommentAverageRating : Microsoft.EntityFrameworkCore.ValueGeneration.ValueGenerator
    {
        public override bool GeneratesTemporaryValues => false;
        protected override object NextValue(EntityEntry entry) => ((CommentSocialAnalytics)entry.Entity).AverageRating;
    }
    #endregion
}
