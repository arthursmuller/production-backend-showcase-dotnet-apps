using Feed.Domain.AggregatesModel.Moderation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Feed.Infra.EntityConfigurations;

public sealed class FeedModerationDecisionStateConfiguration
    : IEntityTypeConfiguration<FeedModerationDecisionState>
{
    public void Configure(EntityTypeBuilder<FeedModerationDecisionState> builder)
    {
        builder.ToTable("FeedModerationDecisionState", table =>
        {
            table.HasCheckConstraint(
                "CK_FeedModerationDecisionState_Applied_NonNegative",
                "`LastAppliedVersion` >= 0");
            table.HasCheckConstraint(
                "CK_FeedModerationDecisionState_Highest_Positive",
                "`HighestObservedVersion` > 0");
            table.HasCheckConstraint(
                "CK_FeedModerationDecisionState_Version_Positive",
                "`Version` > 0");
        });
        builder.HasKey(state => new { state.CaseId, state.ResourceKind, state.ResourceId });
        builder.Property(state => state.CaseId)
            .HasColumnType("char(36)")
            .UseCollation("ascii_general_ci");
        builder.Property(state => state.ResourceKind)
            .HasMaxLength(16)
            .UseCollation("ascii_bin")
            .IsRequired();
        builder.Property(state => state.ResourceId)
            .HasMaxLength(FeedModerationResourceKinds.MaximumResourceIdLength)
            .UseCollation("ascii_bin")
            .IsRequired();
        builder.Property(state => state.Action)
            .HasMaxLength(32)
            .UseCollation("ascii_bin")
            .IsRequired();
        builder.Property(state => state.Status)
            .HasMaxLength(16)
            .UseCollation("ascii_bin")
            .IsRequired();
        builder.Property(state => state.LastSourceOccurredAtUtc)
            .HasColumnType("datetime(6)");
        builder.Property(state => state.CreatedAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired();
        builder.Property(state => state.UpdatedAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired();
        builder.Property(state => state.Version)
            .HasDefaultValue(1L)
            .IsConcurrencyToken();
        builder.HasIndex(state => new
        {
            state.ResourceKind,
            state.ResourceId,
            state.IsSuppressed,
        })
            .HasDatabaseName("IX_FeedModerationDecisionState_Resource_Suppressed");
        builder.HasIndex(state => new { state.Status, state.UpdatedAtUtc, state.CaseId })
            .HasDatabaseName("IX_FeedModerationDecisionState_Status_Updated_Case");
    }
}

public sealed class FeedModerationDecisionGapConfiguration
    : IEntityTypeConfiguration<FeedModerationDecisionGap>
{
    public void Configure(EntityTypeBuilder<FeedModerationDecisionGap> builder)
    {
        builder.ToTable("FeedModerationDecisionGap", table =>
            table.HasCheckConstraint(
                "CK_FeedModerationDecisionGap_Version_Positive",
                "`DecisionVersion` > 0"));
        builder.HasKey(gap => new
        {
            gap.CaseId,
            gap.ResourceKind,
            gap.ResourceId,
            gap.DecisionVersion,
        });
        builder.Property(gap => gap.CaseId)
            .HasColumnType("char(36)")
            .UseCollation("ascii_general_ci");
        builder.Property(gap => gap.ResourceKind)
            .HasMaxLength(16)
            .UseCollation("ascii_bin")
            .IsRequired();
        builder.Property(gap => gap.ResourceId)
            .HasMaxLength(FeedModerationResourceKinds.MaximumResourceIdLength)
            .UseCollation("ascii_bin")
            .IsRequired();
        builder.Property(gap => gap.Action)
            .HasMaxLength(32)
            .UseCollation("ascii_bin")
            .IsRequired();
        builder.Property(gap => gap.SourceOccurredAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired();
        builder.Property(gap => gap.RecordedAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired();
    }
}
