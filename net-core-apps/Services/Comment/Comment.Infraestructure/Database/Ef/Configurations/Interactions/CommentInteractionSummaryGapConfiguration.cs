using Comment.Domain.AggregatesModel.Interactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comment.Infra.Persistence.Configurations.Interactions;

internal sealed class CommentInteractionSummaryGapConfiguration
    : IEntityTypeConfiguration<CommentInteractionSummaryGap>
{
    public void Configure(EntityTypeBuilder<CommentInteractionSummaryGap> builder)
    {
        builder.ToTable("CommentInteractionSummaryGaps");
        builder.HasKey(x => new { x.CommentId, x.SummaryVersion });
        builder.Property(x => x.OccurredAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.HasOne<CommentInteractionSummaryProjection>()
            .WithMany()
            .HasForeignKey(x => x.CommentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.OccurredAtUtc)
            .HasDatabaseName("IX_CommentInteractionSummaryGap_Occurred");
    }
}
