using Feed.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Feed.Infra.EntityConfigurations
{
    public sealed class FeedEngagementSummaryConfiguration
        : IEntityTypeConfiguration<FeedEngagementSummary>
    {
        public void Configure(EntityTypeBuilder<FeedEngagementSummary> builder)
        {
            builder.ToTable("FeedEngagementSummary", table =>
            {
                table.HasCheckConstraint(
                    "CK_FeedEngagementSummary_ResourceType",
                    "`ResourceType` IN ('comment', 'post')");
                table.HasCheckConstraint(
                    "CK_FeedEngagementSummary_Counts_NonNegative",
                    "`LikeCount` >= 0 AND `DislikeCount` >= 0 AND `RatingCount` >= 0 " +
                    "AND `RatingTotal` >= 0 AND `ViewCount` >= 0 AND `CommentCount` >= 0 " +
                    "AND `ShareCount` >= 0");
                table.HasCheckConstraint(
                    "CK_FeedEngagementSummary_RatingScale",
                    "(`RatingCount` = 0 AND `RatingTotal` = 0) " +
                    "OR (`RatingCount` > 0 AND `RatingTotal` >= `RatingCount` " +
                    "AND `RatingTotal` <= `RatingCount` * 6)");
                table.HasCheckConstraint(
                    "CK_FeedEngagementSummary_Versions",
                    "`LastAppliedVersion` >= 0 AND `HighestObservedVersion` >= `LastAppliedVersion` " +
                    "AND `Version` > 0");
                table.HasCheckConstraint(
                    "CK_FeedEngagementSummary_Status",
                    "`Status` IN ('uninitialized', 'healthy', 'gap', 'quarantined')");
            });

            builder.HasKey(item => new { item.ResourceType, item.ResourceId });
            builder.Property(item => item.ResourceType)
                .HasMaxLength(FeedSchemaLimits.TypeLength)
                .IsRequired();
            builder.Property(item => item.ResourceId)
                .HasMaxLength(FeedEngagementResourceTypes.MaximumIdLength)
                .IsRequired();
            builder.Property(item => item.Status)
                .HasMaxLength(FeedSchemaLimits.StateLength)
                .IsRequired();
            builder.Property(item => item.LastEventId)
                .HasColumnType("char(36)")
                .UseCollation("ascii_general_ci");
            builder.Property(item => item.LastSourceOccurredAtUtc).HasColumnType("datetime(6)");
            builder.Property(item => item.CreatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(item => item.UpdatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(item => item.Version)
                .HasDefaultValue(1L)
                .IsConcurrencyToken();

            builder.HasIndex(item => item.LastEventId)
                .IsUnique()
                .HasDatabaseName("UX_FeedEngagementSummary_LastEventId");
            builder.HasIndex(item => new { item.Status, item.UpdatedAtUtc })
                .HasDatabaseName("IX_FeedEngagementSummary_Status_Updated");
        }
    }

    public sealed class FeedEngagementSummaryEventQuarantineConfiguration
        : IEntityTypeConfiguration<FeedEngagementSummaryEventQuarantine>
    {
        public void Configure(EntityTypeBuilder<FeedEngagementSummaryEventQuarantine> builder)
        {
            builder.ToTable("FeedEngagementSummaryEventQuarantine", table =>
            {
                table.HasCheckConstraint(
                    "CK_FeedEngagementSummaryEventQuarantine_ResourceType",
                    "`ResourceType` IN ('comment', 'post')");
                table.HasCheckConstraint(
                    "CK_FeedEngagementSummaryEventQuarantine_Counts_NonNegative",
                    "`LikeCount` >= 0 AND `DislikeCount` >= 0 AND `RatingCount` >= 0 " +
                    "AND `RatingTotal` >= 0 AND `ViewCount` >= 0 AND `CommentCount` >= 0 " +
                    "AND `ShareCount` >= 0");
                table.HasCheckConstraint(
                    "CK_FeedEngagementSummaryEventQuarantine_RatingScale",
                    "(`RatingCount` = 0 AND `RatingTotal` = 0) " +
                    "OR (`RatingCount` > 0 AND `RatingTotal` >= `RatingCount` " +
                    "AND `RatingTotal` <= `RatingCount` * 6)");
                table.HasCheckConstraint(
                    "CK_FeedEngagementSummaryEventQuarantine_Versions",
                    "`SummaryVersion` > 0 AND `ExpectedVersion` > 0");
            });

            builder.HasKey(item => item.Id);
            builder.Property(item => item.Id).ValueGeneratedOnAdd();
            builder.Property(item => item.EventId)
                .HasColumnType("char(36)")
                .UseCollation("ascii_general_ci")
                .IsRequired();
            builder.Property(item => item.ResourceType)
                .HasMaxLength(FeedSchemaLimits.TypeLength)
                .IsRequired();
            builder.Property(item => item.ResourceId)
                .HasMaxLength(FeedEngagementResourceTypes.MaximumIdLength)
                .IsRequired();
            builder.Property(item => item.ReasonCode)
                .HasMaxLength(FeedSchemaLimits.ErrorCodeLength)
                .IsRequired();
            builder.Property(item => item.SourceOccurredAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(item => item.RecordedAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(item => item.ResolvedAtUtc).HasColumnType("datetime(6)");
            builder.Property(item => item.Resolution).HasMaxLength(FeedSchemaLimits.ErrorCodeLength);

            builder.HasIndex(item => item.EventId)
                .IsUnique()
                .HasDatabaseName("UX_FeedEngagementSummaryEventQuarantine_EventId");
            builder.HasIndex(item => new
            {
                item.ResourceType,
                item.ResourceId,
                item.ResolvedAtUtc,
                item.SummaryVersion,
            })
                .HasDatabaseName("IX_FeedEngagementSummaryEventQuarantine_Drain");
        }
    }
}
