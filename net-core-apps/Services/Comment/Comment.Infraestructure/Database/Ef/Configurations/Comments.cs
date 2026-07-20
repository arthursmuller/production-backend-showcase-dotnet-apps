using Comment.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comment.Infra.EntityConfigurations
{
    public sealed class CommentConfiguration : BaseConfiguration, IEntityTypeConfiguration<CommentPost>
    {
        public void Configure(EntityTypeBuilder<CommentPost> builder)
        {
            ConfigureBaseEntity(builder, "CommentPost");
            builder.Ignore(comment => comment.DomainEvents);

            builder.Property(comment => comment.AuthorUserId).IsRequired();
            builder.Property(comment => comment.TargetType)
                .HasMaxLength(CommentFieldLimits.TargetType)
                .IsRequired();
            builder.Property(comment => comment.TargetId)
                .HasMaxLength(CommentFieldLimits.TargetId)
                .IsRequired();
            builder.Property(comment => comment.Description)
                .HasMaxLength(CommentFieldLimits.Description)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(comment => comment.GifUrl)
                .HasMaxLength(CommentFieldLimits.Url)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(comment => comment.VisibilityLevelId).IsRequired();
            builder.Property(comment => comment.PublishedOnFeed)
                .IsRequired()
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(comment => comment.CategoryMentionsPublished)
                .IsRequired()
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(comment => comment.Version)
                .IsRequired()
                .IsConcurrencyToken();

            ConfigureOwnedUser(builder, comment => comment.User);
            builder.ToTable("CommentPost", table => table.HasCheckConstraint(
                "CK_CmtPost_VisibilityLevel",
                "`VisibilityLevelId` IN (1, 2, 3, 4)"));

            builder.OwnsMany(comment => comment.Categories, categories =>
            {
                categories.ToTable("CategoryName");
                categories.WithOwner().HasForeignKey("CommentPostId");
                categories.Property<int>("Id").ValueGeneratedOnAdd();
                categories.HasKey("CommentPostId", "Id");
                categories.Property(category => category.Name)
                    .HasMaxLength(CommentFieldLimits.CategoryName)
                    .IsRequired();
                categories.HasIndex("CommentPostId", nameof(CategoryName.Name))
                    .IsUnique()
                    .HasDatabaseName("UX_CategoryName_CommentPostId_Name");
            });

            builder.OwnsMany(comment => comment.Mentions, mentions =>
            {
                mentions.ToTable("CommentMentions");
                mentions.WithOwner().HasForeignKey("CommentPostId");
                mentions.Property<int>("Id").ValueGeneratedOnAdd();
                mentions.HasKey("CommentPostId", "Id");
                mentions.Property(mention => mention.UserId).IsRequired();
                mentions.Property(mention => mention.UserName)
                    .HasMaxLength(CommentFieldLimits.MentionUserName)
                    .IsRequired();
                mentions.HasIndex("CommentPostId", nameof(UserMention.UserId))
                    .IsUnique()
                    .HasDatabaseName("UX_CommentMentions_CommentPostId_UserId");
            });

            builder.HasMany(comment => comment.MediaItems)
                .WithOne()
                .HasForeignKey("CommentPostId")
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_CmtMedia_Comment");

            builder.HasIndex(comment => new
            {
                comment.TargetType,
                comment.TargetId,
                comment.VisibilityLevelId,
                comment.CreatedDate,
                comment.Id,
            })
                .IsDescending(false, false, false, true, true)
                .HasDatabaseName("IX_CommentPost_Target_Visibility_Created_Id");
            builder.HasIndex(comment => new
            {
                comment.AuthorUserId,
                comment.CreatedDate,
                comment.Id,
            })
                .IsDescending(false, true, true)
                .HasDatabaseName("IX_CommentPost_Author_Created_Id");
            builder.HasIndex(comment => new
            {
                comment.VisibilityLevelId,
                comment.CreatedDate,
                comment.Id,
            })
                .IsDescending(false, true, true)
                .HasDatabaseName("IX_CommentPost_Visibility_Created_Id");

            // Public reads fail closed. Owner/command repositories deliberately opt
            // out after applying their authenticated owner predicates.
            builder.HasQueryFilter(comment =>
                comment.VisibilityLevelId == VisibilityLevel.Global.Id);
        }
    }

    public sealed class CommentMediaItemEntityConfiguration
        : BaseConfiguration, IEntityTypeConfiguration<CommentMediaItem>
    {
        public void Configure(EntityTypeBuilder<CommentMediaItem> builder)
        {
            ConfigureBaseEntity(builder, "CommentMediaItem");
            builder.Property(media => media.Order).IsRequired();
            builder.Property(media => media.Url)
                .IsRequired()
                .HasMaxLength(CommentFieldLimits.Url);
            builder.Property(media => media.FileName).IsRequired().HasMaxLength(255);
            builder.Property(media => media.MediaType).IsRequired().HasConversion<int>();
            builder.Property(media => media.ThumbnailUrl).HasMaxLength(CommentFieldLimits.Url);
            builder.Property(media => media.DurationSeconds);
            builder.Property(media => media.MimeType).IsRequired().HasMaxLength(100);
            builder.HasIndex("CommentPostId", nameof(CommentMediaItem.Order))
                .IsUnique()
                .HasDatabaseName("UX_CommentMediaItem_CommentPostId_Order");
        }
    }

}
