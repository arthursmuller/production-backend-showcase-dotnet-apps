using Comment.Domain.AggregatesModel.Moderation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comment.Infra.Persistence.Configurations.Moderation;

internal sealed class CommentModerationDecisionProjectionConfiguration
    : IEntityTypeConfiguration<CommentModerationDecisionProjection>
{
    public void Configure(EntityTypeBuilder<CommentModerationDecisionProjection> builder)
    {
        builder.ToTable("CommentModerationDecisionProjection", table =>
        {
            table.HasCheckConstraint(
                "CK_CmtModDecision_Action",
                "`Action` BETWEEN 1 AND 5");
            table.HasCheckConstraint(
                "CK_CmtModDecision_Versions",
                "`AppliedVersion` >= 0 AND `HighestSeenVersion` >= `AppliedVersion`");
            table.HasCheckConstraint(
                "CK_CmtModDecision_Gap",
                "(`HasGap` = 1 AND `IsSuppressed` = 1 AND `GapDetectedAtUtc` IS NOT NULL) OR (`HasGap` = 0 AND `GapDetectedAtUtc` IS NULL)");
            table.HasCheckConstraint(
                "CK_CmtModDecision_Suppression",
                "`IsSuppressed` = (`HasGap` = 1 OR `Action` <> 1)");
        });
        builder.HasKey(x => new { x.CaseId, x.CommentId });
        builder.Property(x => x.CaseId)
            .HasColumnType("char(36)")
            .UseCollation("ascii_general_ci");
        builder.Property(x => x.CommentId).IsRequired();
        builder.Property(x => x.Action).HasConversion<byte>().IsRequired();
        builder.Property(x => x.AppliedVersion).IsConcurrencyToken().IsRequired();
        builder.Property(x => x.HighestSeenVersion).IsRequired();
        builder.Property(x => x.HasGap).IsRequired();
        builder.Property(x => x.IsSuppressed).IsRequired();
        builder.Property(x => x.DecisionOccurredAtUtc).HasColumnType("datetime(6)");
        builder.Property(x => x.GapDetectedAtUtc).HasColumnType("datetime(6)");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.HasIndex(x => new { x.CommentId, x.IsSuppressed })
            .HasDatabaseName("IX_CmtModDecision_Comment_Suppressed");
        builder.HasIndex(x => new { x.HasGap, x.GapDetectedAtUtc })
            .HasDatabaseName("IX_CmtModDecision_Reconciliation");
    }
}

internal sealed class CommentModerationDecisionGapConfiguration
    : IEntityTypeConfiguration<CommentModerationDecisionGap>
{
    public void Configure(EntityTypeBuilder<CommentModerationDecisionGap> builder)
    {
        builder.ToTable("CommentModerationDecisionGap", table =>
        {
            table.HasCheckConstraint(
                "CK_CmtModGap_Action",
                "`Action` BETWEEN 1 AND 5");
            table.HasCheckConstraint(
                "CK_CmtModGap_Version",
                "`DecisionVersion` > 0");
        });
        builder.HasKey(x => new { x.CaseId, x.CommentId, x.DecisionVersion });
        builder.Property(x => x.CaseId)
            .HasColumnType("char(36)")
            .UseCollation("ascii_general_ci");
        builder.Property(x => x.Action).HasConversion<byte>().IsRequired();
        builder.Property(x => x.OccurredAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.Property(x => x.ObservedAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.HasOne<CommentModerationDecisionProjection>()
            .WithMany()
            .HasForeignKey(x => new { x.CaseId, x.CommentId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.CaseId, x.CommentId, x.DecisionVersion })
            .HasDatabaseName("IX_CmtModGap_Drain");
    }
}
