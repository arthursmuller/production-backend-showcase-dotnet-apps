using Comment.Domain.AggregatesModel.Interactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comment.Infra.Persistence.Configurations.Interactions;

internal sealed class CommentInteractionSummaryProjectionConfiguration
    : IEntityTypeConfiguration<CommentInteractionSummaryProjection>
{
    public void Configure(EntityTypeBuilder<CommentInteractionSummaryProjection> builder)
    {
        builder.ToTable("CommentInteractionSummaryProjections", table =>
        {
            table.HasCheckConstraint(
                "CK_CommentInteractionSummary_Nonnegative",
                "`LikeCount` >= 0 AND `DislikeCount` >= 0 AND `RatingCount` >= 0 AND `RatingTotal` >= 0 AND `ViewCount` >= 0 AND `CommentCount` >= 0 AND `ShareCount` >= 0");
            table.HasCheckConstraint(
                "CK_CommentInteractionSummary_Versions",
                "`AppliedVersion` > 0 AND `HighestSeenVersion` >= `AppliedVersion`");
        });
        builder.HasKey(x => x.CommentId);
        builder.Property(x => x.CommentId).ValueGeneratedNever();
        builder.Property(x => x.AppliedVersion).IsConcurrencyToken().IsRequired();
        builder.Property(x => x.HighestSeenVersion).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.HasIndex(x => new { x.HasGap, x.UpdatedAtUtc })
            .HasDatabaseName("IX_CommentInteractionSummary_Gap_Updated");
    }
}
