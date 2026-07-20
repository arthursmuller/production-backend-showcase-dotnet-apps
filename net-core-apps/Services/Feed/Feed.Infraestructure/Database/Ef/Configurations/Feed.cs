using Feed.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Feed.Infra.EntityConfigurations
{
    public sealed class FeedActivityConfiguration : IEntityTypeConfiguration<FeedActivity>
    {
        public void Configure(EntityTypeBuilder<FeedActivity> builder)
        {
            builder.ToTable("FeedActivity", table =>
            {
                table.HasCheckConstraint("CK_FeedActivity_Actor_Positive", "`ActorUserId` > 0");
                table.HasCheckConstraint("CK_FeedActivity_SourceVersion_NonNegative", "`SourceVersion` >= 0");
                table.HasCheckConstraint("CK_FeedActivity_Version_Positive", "`Version` > 0");
            });
            builder.HasKey(activity => activity.Id);
            builder.Property(activity => activity.Id).HasColumnName("Id");
            builder.Property(activity => activity.TargetType)
                .HasColumnName("TargetType")
                .HasMaxLength(FeedSchemaLimits.TypeLength)
                .IsRequired();
            builder.Property(activity => activity.TargetId)
                .HasColumnName("TargetId")
                .HasMaxLength(FeedSchemaLimits.TargetIdLength)
                .IsRequired();
            builder.Property(activity => activity.ActivityKind)
                .HasColumnName("ActivityKind")
                .HasMaxLength(FeedSchemaLimits.EventKindLength)
                .IsRequired();
            builder.Property(activity => activity.ActorUserId)
                .HasColumnName("ActorUserId")
                .IsRequired();
            builder.Property(activity => activity.AudiencePolicy)
                .HasColumnName("AudiencePolicy")
                .HasMaxLength(FeedSchemaLimits.AudiencePolicyLength)
                .IsRequired();
            builder.Property(activity => activity.SourceStreamId)
                .HasColumnType("char(36)")
                .UseCollation("ascii_general_ci");
            builder.Property(activity => activity.LastEventId)
                .HasColumnType("char(36)")
                .UseCollation("ascii_general_ci");
            builder.Property(activity => activity.SourceVersion)
                .HasDefaultValue(0L)
                .IsRequired();
            builder.Property(activity => activity.RankKey)
                .HasDefaultValue(0L)
                .IsRequired();
            builder.Property(activity => activity.DeliveryMode)
                .HasMaxLength(FeedSchemaLimits.MaterializationModeLength)
                .HasDefaultValue(FeedDeliveryModes.Push)
                .IsRequired();
            builder.Property(activity => activity.Summary)
                .HasMaxLength(FeedSchemaLimits.DescriptionLength)
                .HasDefaultValue(string.Empty)
                .IsRequired();
            builder.Property(activity => activity.PublishedAtUtc)
                .HasColumnName("PublishedAtUtc")
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(activity => activity.IsTombstoned)
                .HasColumnName("IsTombstoned")
                .IsRequired();
            builder.Property(activity => activity.TombstonedAtUtc)
                .HasColumnName("TombstonedAtUtc")
                .HasColumnType("datetime(6)");
            builder.Property(activity => activity.CreatedAtUtc)
                .HasColumnName("CreatedAtUtc")
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(activity => activity.UpdatedAtUtc)
                .HasColumnName("UpdatedAtUtc")
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(activity => activity.Version)
                .HasColumnName("Version")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();

            builder.HasIndex(activity => new
            {
                activity.TargetType,
                activity.TargetId,
                activity.ActivityKind,
            })
                .IsUnique()
                .HasDatabaseName("UX_FeedActivity_Target_Kind");
            builder.HasIndex(activity => new { activity.TargetType, activity.TargetId })
                .HasDatabaseName("IX_FeedActivity_Target");
            builder.HasIndex(activity => new
            {
                activity.ActorUserId,
                activity.PublishedAtUtc,
                activity.Id,
            })
                .IsDescending(false, true, true)
                .HasDatabaseName("IX_FeedActivity_Actor_Published_Id");
            builder.HasIndex(activity => activity.SourceStreamId)
                .IsUnique()
                .HasDatabaseName("UX_FeedActivity_SourceStreamId");
            builder.HasIndex(activity => new
            {
                activity.ActorUserId,
                activity.IsTombstoned,
                activity.DeliveryMode,
                activity.RankKey,
                activity.PublishedAtUtc,
                activity.Id,
            })
                .IsDescending(false, false, false, true, true, true)
                .HasDatabaseName("IX_FeedActivity_Pull_Cursor");
            builder.HasIndex(activity => new { activity.ActorUserId, activity.Id })
                .HasDatabaseName("IX_FeedActivity_Actor_Id");
        }
    }

    public sealed class FeedEntryConfiguration : IEntityTypeConfiguration<FeedEntry>
    {
        public void Configure(EntityTypeBuilder<FeedEntry> builder)
        {
            builder.ToTable("FeedEntry", table =>
            {
                table.HasCheckConstraint("CK_FeedEntry_Recipient_Positive", "`RecipientUserId` > 0");
                table.HasCheckConstraint("CK_FeedEntry_Version_Positive", "`Version` > 0");
            });
            builder.HasKey(entry => entry.Id);
            builder.Property(entry => entry.Id).HasColumnName("Id");
            builder.Property(entry => entry.RecipientUserId)
                .HasColumnName("RecipientUserId")
                .IsRequired();
            builder.Property(entry => entry.ActivityId)
                .HasColumnName("ActivityId")
                .IsRequired();
            builder.Property(entry => entry.MaterializationMode)
                .HasColumnName("MaterializationMode")
                .HasMaxLength(FeedSchemaLimits.MaterializationModeLength)
                .IsRequired();
            builder.Property(entry => entry.RankKey)
                .HasColumnName("RankKey")
                .IsRequired();
            builder.Property(entry => entry.PublishedAtUtc)
                .HasColumnName("PublishedAtUtc")
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(entry => entry.SeenAtUtc)
                .HasColumnName("SeenAtUtc")
                .HasColumnType("datetime(6)");
            builder.Property(entry => entry.RevokedAtUtc)
                .HasColumnName("RevokedAtUtc")
                .HasColumnType("datetime(6)");
            builder.Property(entry => entry.CreatedAtUtc)
                .HasColumnName("CreatedAtUtc")
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(entry => entry.UpdatedAtUtc)
                .HasColumnName("UpdatedAtUtc")
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(entry => entry.Version)
                .HasColumnName("Version")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();

            builder.HasOne(entry => entry.Activity)
                .WithMany()
                .HasForeignKey(entry => entry.ActivityId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(entry => new { entry.RecipientUserId, entry.ActivityId })
                .IsUnique()
                .HasDatabaseName("UX_FeedEntry_Recipient_Activity");
            builder.HasIndex(entry => entry.ActivityId)
                .HasDatabaseName("IX_FeedEntry_ActivityId");
            builder.HasIndex(entry => new { entry.ActivityId, entry.Id })
                .HasDatabaseName("IX_FeedEntry_Activity_Id");
            builder.HasIndex(entry => new { entry.RecipientUserId, entry.Id })
                .HasDatabaseName("IX_FeedEntry_Recipient_Id");
            builder.HasIndex(entry => new
            {
                entry.RecipientUserId,
                entry.RevokedAtUtc,
                entry.SeenAtUtc,
                entry.RankKey,
                entry.PublishedAtUtc,
                entry.Id,
            })
                .IsDescending(false, false, false, true, true, true)
                .HasDatabaseName("IX_FeedEntry_Recipient_State_Cursor");
        }
    }

    public sealed class FeedAudienceEdgeConfiguration : IEntityTypeConfiguration<FeedAudienceEdge>
    {
        public void Configure(EntityTypeBuilder<FeedAudienceEdge> builder)
        {
            builder.ToTable("FeedAudienceEdge", table =>
            {
                table.HasCheckConstraint("CK_FeedAudienceEdge_Owner_Positive", "`OwnerUserId` > 0");
                table.HasCheckConstraint("CK_FeedAudienceEdge_Viewer_Positive", "`ViewerUserId` > 0");
                table.HasCheckConstraint("CK_FeedAudienceEdge_Directional", "`OwnerUserId` <> `ViewerUserId`");
                table.HasCheckConstraint("CK_FeedAudienceEdge_SourceVersion_Positive", "`SourceVersion` > 0");
                table.HasCheckConstraint("CK_FeedAudienceEdge_Version_Positive", "`Version` > 0");
            });
            builder.HasKey(edge => edge.Id);
            builder.Property(edge => edge.Id).HasColumnName("Id");
            builder.Property(edge => edge.OwnerUserId).HasColumnName("OwnerUserId").IsRequired();
            builder.Property(edge => edge.ViewerUserId).HasColumnName("ViewerUserId").IsRequired();
            builder.Property(edge => edge.RelationshipKind)
                .HasColumnName("RelationshipKind")
                .HasMaxLength(FeedSchemaLimits.RelationshipKindLength)
                .IsRequired();
            builder.Property(edge => edge.State)
                .HasColumnName("State")
                .HasMaxLength(FeedSchemaLimits.StateLength)
                .IsRequired();
            builder.Property(edge => edge.SourceStreamId)
                .HasColumnName("SourceStreamId")
                .HasColumnType("char(36)")
                .UseCollation("ascii_general_ci");
            builder.Property(edge => edge.LastEventId)
                .HasColumnName("LastEventId")
                .HasColumnType("char(36)")
                .UseCollation("ascii_general_ci");
            builder.Property(edge => edge.SourceVersion).HasColumnName("SourceVersion").IsRequired();
            builder.Property(edge => edge.SourceOccurredAtUtc)
                .HasColumnName("SourceOccurredAtUtc")
                .HasColumnType("datetime(6)");
            builder.Property(edge => edge.CreatedAtUtc)
                .HasColumnName("CreatedAtUtc")
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(edge => edge.UpdatedAtUtc)
                .HasColumnName("UpdatedAtUtc")
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(edge => edge.Version)
                .HasColumnName("Version")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();

            builder.HasIndex(edge => new
            {
                edge.OwnerUserId,
                edge.ViewerUserId,
                edge.RelationshipKind,
            })
                .IsUnique()
                .HasDatabaseName("UX_FeedAudienceEdge_Direction_Kind");
            builder.HasIndex(edge => new
            {
                edge.ViewerUserId,
                edge.OwnerUserId,
                edge.State,
                edge.RelationshipKind,
            })
                .HasDatabaseName("IX_FeedAudienceEdge_Reverse_State");
            builder.HasIndex(edge => edge.SourceStreamId)
                .IsUnique()
                .HasDatabaseName("UX_FeedAudienceEdge_SourceStreamId");
            builder.HasIndex(edge => new { edge.OwnerUserId, edge.Id })
                .HasDatabaseName("IX_FeedAudienceEdge_Owner_Id");
            builder.HasIndex(edge => new { edge.ViewerUserId, edge.Id })
                .HasDatabaseName("IX_FeedAudienceEdge_Viewer_Id");
        }
    }

    public sealed class FeedAudienceBootstrapStateConfiguration
        : IEntityTypeConfiguration<FeedAudienceBootstrapState>
    {
        public void Configure(EntityTypeBuilder<FeedAudienceBootstrapState> builder)
        {
            builder.ToTable("FeedAudienceBootstrapState", table =>
            {
                table.HasCheckConstraint(
                    "CK_FeedAudienceBootstrapState_Singleton",
                    "`Id` = 1");
                table.HasCheckConstraint(
                    "CK_FeedAudienceBootstrapState_Cursors",
                    "`NextCursor` >= 0 AND (`SnapshotHighWatermark` IS NULL OR `SnapshotHighWatermark` >= `NextCursor`)");
                table.HasCheckConstraint(
                    "CK_FeedAudienceBootstrapState_Version",
                    "`Version` > 0");
            });
            builder.HasKey(state => state.Id);
            builder.Property(state => state.Id).ValueGeneratedNever();
            builder.Property(state => state.Status)
                .HasMaxLength(FeedSchemaLimits.StateLength)
                .IsRequired();
            builder.Property(state => state.CycleId)
                .HasColumnType("char(36)")
                .UseCollation("ascii_general_ci");
            builder.Property(state => state.CycleInProgress).IsRequired();
            builder.Property(state => state.NextCursor).IsRequired();
            builder.Property(state => state.SnapshotHighWatermark);
            builder.Property(state => state.SnapshotAsOfUtc).HasColumnType("datetime(6)");
            builder.Property(state => state.CycleStartedAtUtc).HasColumnType("datetime(6)");
            builder.Property(state => state.LastPageAtUtc).HasColumnType("datetime(6)");
            builder.Property(state => state.LastCompletedAtUtc).HasColumnType("datetime(6)");
            builder.Property(state => state.LastErrorCode)
                .HasMaxLength(FeedSchemaLimits.ErrorCodeLength);
            builder.Property(state => state.LeaseOwner)
                .HasMaxLength(FeedSchemaLimits.LeaseOwnerLength);
            builder.Property(state => state.LeaseToken)
                .HasColumnType("char(36)")
                .UseCollation("ascii_general_ci");
            builder.Property(state => state.LeaseExpiresAtUtc).HasColumnType("datetime(6)");
            builder.Property(state => state.CreatedAtUtc)
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(state => state.UpdatedAtUtc)
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(state => state.Version)
                .HasDefaultValue(1L)
                .IsConcurrencyToken()
                .IsRequired();
            builder.HasIndex(state => new { state.Status, state.LastCompletedAtUtc })
                .HasDatabaseName("IX_FeedAudienceBootstrapState_Health");
        }
    }

    public sealed class FeedRelationshipCheckpointConfiguration
        : IEntityTypeConfiguration<FeedRelationshipCheckpoint>
    {
        public void Configure(EntityTypeBuilder<FeedRelationshipCheckpoint> builder)
        {
            builder.ToTable("FeedRelationshipCheckpoint", table =>
            {
                table.HasCheckConstraint("CK_FeedRelationshipCheckpoint_Owner", "`OwnerUserId` > 0");
                table.HasCheckConstraint("CK_FeedRelationshipCheckpoint_Viewer", "`ViewerUserId` > 0");
                table.HasCheckConstraint(
                    "CK_FeedRelationshipCheckpoint_Directional",
                    "`OwnerUserId` <> `ViewerUserId`");
                table.HasCheckConstraint(
                    "CK_FeedRelationshipCheckpoint_Versions",
                    "`LastAppliedVersion` >= 0 AND `HighestObservedVersion` >= `LastAppliedVersion`");
                table.HasCheckConstraint("CK_FeedRelationshipCheckpoint_Version", "`Version` > 0");
            });
            builder.HasKey(checkpoint => checkpoint.StreamId);
            builder.Property(checkpoint => checkpoint.StreamId)
                .HasColumnType("char(36)")
                .UseCollation("ascii_general_ci");
            builder.Property(checkpoint => checkpoint.OwnerUserId).IsRequired();
            builder.Property(checkpoint => checkpoint.ViewerUserId).IsRequired();
            builder.Property(checkpoint => checkpoint.RelationshipKind)
                .HasMaxLength(FeedSchemaLimits.RelationshipKindLength)
                .IsRequired();
            builder.Property(checkpoint => checkpoint.LastAppliedVersion)
                .HasDefaultValue(0L)
                .IsRequired();
            builder.Property(checkpoint => checkpoint.HighestObservedVersion)
                .HasDefaultValue(0L)
                .IsRequired();
            builder.Property(checkpoint => checkpoint.Status)
                .HasMaxLength(FeedSchemaLimits.StateLength)
                .IsRequired();
            builder.Property(checkpoint => checkpoint.GapFromVersion);
            builder.Property(checkpoint => checkpoint.GapToVersion);
            builder.Property(checkpoint => checkpoint.LastEventId)
                .HasColumnType("char(36)")
                .UseCollation("ascii_general_ci");
            builder.Property(checkpoint => checkpoint.LastSourceOccurredAtUtc)
                .HasColumnType("datetime(6)");
            builder.Property(checkpoint => checkpoint.LastConfirmedAtUtc)
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(checkpoint => checkpoint.LastReconciledAtUtc)
                .HasColumnType("datetime(6)");
            builder.Property(checkpoint => checkpoint.CreatedAtUtc)
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(checkpoint => checkpoint.UpdatedAtUtc)
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(checkpoint => checkpoint.Version)
                .HasDefaultValue(1L)
                .IsConcurrencyToken()
                .IsRequired();

            builder.HasIndex(checkpoint => new
            {
                checkpoint.OwnerUserId,
                checkpoint.ViewerUserId,
                checkpoint.RelationshipKind,
            })
                .IsUnique()
                .HasDatabaseName("UX_FeedRelationshipCheckpoint_Direction_Kind");
            builder.HasIndex(checkpoint => new { checkpoint.Status, checkpoint.UpdatedAtUtc })
                .HasDatabaseName("IX_FeedRelationshipCheckpoint_Status_Updated");
            builder.HasIndex(checkpoint => new { checkpoint.OwnerUserId, checkpoint.StreamId })
                .HasDatabaseName("IX_FeedRelationshipCheckpoint_Owner_Stream");
            builder.HasIndex(checkpoint => new { checkpoint.ViewerUserId, checkpoint.StreamId })
                .HasDatabaseName("IX_FeedRelationshipCheckpoint_Viewer_Stream");
        }
    }

    public sealed class FeedRelationshipEventQuarantineConfiguration
        : IEntityTypeConfiguration<FeedRelationshipEventQuarantine>
    {
        public void Configure(EntityTypeBuilder<FeedRelationshipEventQuarantine> builder)
        {
            builder.ToTable("FeedRelationshipEventQuarantine", table =>
            {
                table.HasCheckConstraint(
                    "CK_FeedRelationshipEventQuarantine_Versions",
                    "`ReceivedVersion` > 0 AND `ExpectedVersion` > 0");
            });
            builder.HasKey(item => item.Id);
            builder.Property(item => item.Id).ValueGeneratedOnAdd();
            builder.Property(item => item.EventId)
                .HasColumnType("char(36)")
                .UseCollation("ascii_general_ci")
                .IsRequired();
            builder.Property(item => item.StreamId)
                .HasColumnType("char(36)")
                .UseCollation("ascii_general_ci")
                .IsRequired();
            builder.Property(item => item.OwnerUserId).IsRequired();
            builder.Property(item => item.ViewerUserId).IsRequired();
            builder.Property(item => item.RelationshipKind)
                .HasMaxLength(FeedSchemaLimits.RelationshipKindLength)
                .IsRequired();
            builder.Property(item => item.State)
                .HasMaxLength(FeedSchemaLimits.StateLength)
                .IsRequired();
            builder.Property(item => item.ReceivedVersion).IsRequired();
            builder.Property(item => item.ExpectedVersion).IsRequired();
            builder.Property(item => item.ReasonCode)
                .HasMaxLength(FeedSchemaLimits.ErrorCodeLength)
                .IsRequired();
            builder.Property(item => item.SourceOccurredAtUtc)
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(item => item.RecordedAtUtc)
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(item => item.ResolvedAtUtc).HasColumnType("datetime(6)");
            builder.Property(item => item.Resolution)
                .HasMaxLength(FeedSchemaLimits.ErrorCodeLength);

            builder.HasIndex(item => item.EventId)
                .IsUnique()
                .HasDatabaseName("UX_FeedRelationshipEventQuarantine_EventId");
            builder.HasIndex(item => new { item.StreamId, item.ReceivedVersion })
                .IsUnique()
                .HasDatabaseName("UX_FeedRelationshipEventQuarantine_Stream_Version");
            builder.HasIndex(item => new { item.ResolvedAtUtc, item.RecordedAtUtc, item.Id })
                .HasDatabaseName("IX_FeedRelationshipEventQuarantine_Resolution_Cursor");
            builder.HasIndex(item => new { item.OwnerUserId, item.Id })
                .HasDatabaseName("IX_FeedRelationshipEventQuarantine_Owner_Id");
            builder.HasIndex(item => new { item.ViewerUserId, item.Id })
                .HasDatabaseName("IX_FeedRelationshipEventQuarantine_Viewer_Id");
        }
    }

    public sealed class FeedUserStateConfiguration : IEntityTypeConfiguration<FeedUserState>
    {
        public void Configure(EntityTypeBuilder<FeedUserState> builder)
        {
            builder.ToTable("FeedUserState", table =>
            {
                table.HasCheckConstraint("CK_FeedUserState_User_Positive", "`UserId` > 0");
                table.HasCheckConstraint("CK_FeedUserState_Revision_NonNegative", "`FeedRevision` >= 0");
                table.HasCheckConstraint("CK_FeedUserState_Version_Positive", "`Version` > 0");
            });
            builder.HasKey(state => state.UserId);
            builder.Property(state => state.UserId)
                .HasColumnName("UserId")
                .ValueGeneratedNever();
            builder.Property(state => state.FeedRevision)
                .HasColumnName("FeedRevision")
                .HasDefaultValue(0L)
                .IsRequired();
            builder.Property(state => state.LastAcknowledgedAtUtc)
                .HasColumnName("LastAcknowledgedAtUtc")
                .HasColumnType("datetime(6)");
            builder.Property(state => state.CompactionState)
                .HasColumnName("CompactionState")
                .HasMaxLength(FeedSchemaLimits.StateLength);
            builder.Property(state => state.CompactionCursor)
                .HasColumnName("CompactionCursor")
                .HasMaxLength(FeedSchemaLimits.CursorLength);
            builder.Property(state => state.CreatedAtUtc)
                .HasColumnName("CreatedAtUtc")
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(state => state.UpdatedAtUtc)
                .HasColumnName("UpdatedAtUtc")
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(state => state.Version)
                .HasColumnName("Version")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();
        }
    }

    public sealed class FanoutWorkItemConfiguration : IEntityTypeConfiguration<FanoutWorkItem>
    {
        public void Configure(EntityTypeBuilder<FanoutWorkItem> builder)
        {
            builder.ToTable("FanoutWorkItem", table =>
            {
                table.HasCheckConstraint("CK_FanoutWorkItem_Attempts_NonNegative", "`AttemptCount` >= 0");
                table.HasCheckConstraint("CK_FanoutWorkItem_Publisher_Positive", "`PublisherUserId` > 0");
                table.HasCheckConstraint("CK_FanoutWorkItem_Version_Positive", "`Version` > 0");
            });
            builder.HasKey(work => work.Id);
            builder.Property(work => work.Id).HasColumnName("Id");
            builder.Property(work => work.SourceEventId)
                .HasColumnName("SourceEventId")
                .HasMaxLength(FeedSchemaLimits.SourceEventIdLength)
                .IsRequired();
            builder.Property(work => work.ActivityId).HasColumnName("ActivityId").IsRequired();
            builder.Property(work => work.State)
                .HasColumnName("State")
                .HasMaxLength(FeedSchemaLimits.StateLength)
                .IsRequired();
            builder.Property(work => work.NextCursor)
                .HasColumnName("NextCursor")
                .HasMaxLength(FeedSchemaLimits.CursorLength);
            builder.Property(work => work.AttemptCount)
                .HasColumnName("AttemptCount")
                .HasDefaultValue(0)
                .IsRequired();
            builder.Property(work => work.LeaseOwner)
                .HasColumnName("LeaseOwner")
                .HasMaxLength(FeedSchemaLimits.LeaseOwnerLength);
            builder.Property(work => work.LeaseExpiresAtUtc)
                .HasColumnName("LeaseExpiresAtUtc")
                .HasColumnType("datetime(6)");
            builder.Property(work => work.LastErrorCode)
                .HasColumnName("LastErrorCode")
                .HasMaxLength(FeedSchemaLimits.ErrorCodeLength);
            builder.Property(work => work.PublisherUserId).IsRequired();
            builder.Property(work => work.WorkKind)
                .HasMaxLength(FeedSchemaLimits.StateLength)
                .HasDefaultValue(FeedWorkKinds.Fanout)
                .IsRequired();
            builder.Property(work => work.NextViewerUserId);
            builder.Property(work => work.AudienceHighWatermarkUserId);
            builder.Property(work => work.LeaseToken)
                .HasColumnType("char(36)")
                .UseCollation("ascii_general_ci");
            builder.Property(work => work.CompletedAtUtc)
                .HasColumnName("CompletedAtUtc")
                .HasColumnType("datetime(6)");
            builder.Property(work => work.CreatedAtUtc)
                .HasColumnName("CreatedAtUtc")
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(work => work.UpdatedAtUtc)
                .HasColumnName("UpdatedAtUtc")
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(work => work.Version)
                .HasColumnName("Version")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();

            builder.HasOne(work => work.Activity)
                .WithMany()
                .HasForeignKey(work => work.ActivityId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(work => work.SourceEventId)
                .IsUnique()
                .HasDatabaseName("UX_FanoutWorkItem_SourceEventId");
            builder.HasIndex(work => work.ActivityId)
                .HasDatabaseName("IX_FanoutWorkItem_ActivityId");
            builder.HasIndex(work => new { work.State, work.LeaseExpiresAtUtc, work.Id })
                .HasDatabaseName("IX_FanoutWorkItem_State_Lease_Id");
            builder.HasIndex(work => new
            {
                work.State,
                work.LeaseExpiresAtUtc,
                work.UpdatedAtUtc,
                work.PublisherUserId,
                work.Id,
            })
                .HasDatabaseName("IX_FanoutWorkItem_FairClaim");
            builder.HasIndex(work => new { work.PublisherUserId, work.Id })
                .HasDatabaseName("IX_FanoutWorkItem_Publisher_Id");
        }
    }

    public sealed class FeedPublisherStateConfiguration
        : IEntityTypeConfiguration<FeedPublisherState>
    {
        public void Configure(EntityTypeBuilder<FeedPublisherState> builder)
        {
            builder.ToTable("FeedPublisherState", table =>
            {
                table.HasCheckConstraint("CK_FeedPublisherState_Publisher", "`PublisherUserId` > 0");
                table.HasCheckConstraint("CK_FeedPublisherState_Audience", "`AudienceCount` >= 0");
                table.HasCheckConstraint("CK_FeedPublisherState_Version", "`Version` > 0");
            });
            builder.HasKey(state => state.PublisherUserId);
            builder.Property(state => state.PublisherUserId).ValueGeneratedNever();
            builder.Property(state => state.DeliveryMode)
                .HasMaxLength(FeedSchemaLimits.MaterializationModeLength)
                .IsRequired();
            builder.Property(state => state.AudienceCount).IsRequired();
            builder.Property(state => state.LastEvaluatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(state => state.CreatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(state => state.UpdatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(state => state.Version).HasDefaultValue(1L).IsConcurrencyToken();
            builder.HasIndex(state => new { state.DeliveryMode, state.UpdatedAtUtc, state.PublisherUserId })
                .HasDatabaseName("IX_FeedPublisherState_Mode_Updated");
        }
    }

    public sealed class FeedRevocationWorkItemConfiguration
        : IEntityTypeConfiguration<FeedRevocationWorkItem>
    {
        public void Configure(EntityTypeBuilder<FeedRevocationWorkItem> builder)
        {
            builder.ToTable("FeedRevocationWorkItem", table =>
            {
                table.HasCheckConstraint("CK_FeedRevocationWorkItem_Priority", "`Priority` > 0");
                table.HasCheckConstraint("CK_FeedRevocationWorkItem_Progress", "`NextEntryId` >= 0");
                table.HasCheckConstraint("CK_FeedRevocationWorkItem_Version", "`Version` > 0");
            });
            builder.HasKey(work => work.Id);
            builder.Property(work => work.SourceEventId).HasMaxLength(FeedSchemaLimits.SourceEventIdLength).IsRequired();
            builder.Property(work => work.RevocationKind).HasMaxLength(FeedSchemaLimits.StateLength).IsRequired();
            builder.Property(work => work.AudiencePolicy).HasMaxLength(FeedSchemaLimits.AudiencePolicyLength);
            builder.Property(work => work.State).HasMaxLength(FeedSchemaLimits.StateLength).IsRequired();
            builder.Property(work => work.LeaseOwner).HasMaxLength(FeedSchemaLimits.LeaseOwnerLength);
            builder.Property(work => work.LeaseToken).HasColumnType("char(36)").UseCollation("ascii_general_ci");
            builder.Property(work => work.LeaseExpiresAtUtc).HasColumnType("datetime(6)");
            builder.Property(work => work.LastErrorCode).HasMaxLength(FeedSchemaLimits.ErrorCodeLength);
            builder.Property(work => work.CompletedAtUtc).HasColumnType("datetime(6)");
            builder.Property(work => work.SourceOccurredAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(work => work.CreatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(work => work.UpdatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(work => work.Version).HasDefaultValue(1L).IsConcurrencyToken();
            builder.HasOne(work => work.Activity)
                .WithMany()
                .HasForeignKey(work => work.ActivityId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasIndex(work => work.SourceEventId)
                .IsUnique()
                .HasDatabaseName("UX_FeedRevocationWorkItem_SourceEventId");
            builder.HasIndex(work => new
            {
                work.State,
                work.Priority,
                work.LeaseExpiresAtUtc,
                work.UpdatedAtUtc,
                work.Id,
            })
                .IsDescending(false, true, false, false, false)
                .HasDatabaseName("IX_FeedRevocationWorkItem_PriorityClaim");
            builder.HasIndex(work => new { work.OwnerUserId, work.Id })
                .HasDatabaseName("IX_FeedRevocationWorkItem_Owner_Id");
            builder.HasIndex(work => new { work.ViewerUserId, work.Id })
                .HasDatabaseName("IX_FeedRevocationWorkItem_Viewer_Id");
        }
    }

    public sealed class FeedMaintenanceCheckpointConfiguration
        : IEntityTypeConfiguration<FeedMaintenanceCheckpoint>
    {
        public void Configure(EntityTypeBuilder<FeedMaintenanceCheckpoint> builder)
        {
            builder.ToTable("FeedMaintenanceCheckpoint", table =>
            {
                table.HasCheckConstraint("CK_FeedMaintenanceCheckpoint_Cursor", "`Cursor` >= 0");
                table.HasCheckConstraint("CK_FeedMaintenanceCheckpoint_Version", "`Version` > 0");
            });
            builder.HasKey(checkpoint => checkpoint.JobName);
            builder.Property(checkpoint => checkpoint.JobName).HasMaxLength(FeedSchemaLimits.StateLength);
            builder.Property(checkpoint => checkpoint.LastCompletedAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(checkpoint => checkpoint.CreatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(checkpoint => checkpoint.UpdatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(checkpoint => checkpoint.Version).HasDefaultValue(1L).IsConcurrencyToken();
        }
    }

    public sealed class FeedActivityEventQuarantineConfiguration
        : IEntityTypeConfiguration<FeedActivityEventQuarantine>
    {
        public void Configure(EntityTypeBuilder<FeedActivityEventQuarantine> builder)
        {
            builder.ToTable("FeedActivityEventQuarantine", table =>
                table.HasCheckConstraint("CK_FeedActivityEventQuarantine_Version", "`StreamVersion` > 0"));
            builder.HasKey(item => item.Id);
            builder.Property(item => item.EventId).HasColumnType("char(36)").UseCollation("ascii_general_ci").IsRequired();
            builder.Property(item => item.StreamId).HasColumnType("char(36)").UseCollation("ascii_general_ci").IsRequired();
            builder.Property(item => item.TargetType).HasMaxLength(FeedSchemaLimits.TypeLength).IsRequired();
            builder.Property(item => item.TargetId).HasMaxLength(FeedSchemaLimits.TargetIdLength).IsRequired();
            builder.Property(item => item.ActivityKind).HasMaxLength(FeedSchemaLimits.EventKindLength).IsRequired();
            builder.Property(item => item.AudiencePolicy).HasMaxLength(FeedSchemaLimits.AudiencePolicyLength).IsRequired();
            builder.Property(item => item.State).HasMaxLength(FeedSchemaLimits.StateLength).IsRequired();
            builder.Property(item => item.PublishedAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(item => item.SourceOccurredAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(item => item.ReasonCode).HasMaxLength(FeedSchemaLimits.ErrorCodeLength).IsRequired();
            builder.Property(item => item.RecordedAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(item => item.ResolvedAtUtc).HasColumnType("datetime(6)");
            builder.HasIndex(item => item.EventId).IsUnique().HasDatabaseName("UX_FeedActivityEventQuarantine_EventId");
            builder.HasIndex(item => new { item.StreamId, item.StreamVersion })
                .IsUnique()
                .HasDatabaseName("UX_FeedActivityEventQuarantine_Stream_Version");
            builder.HasIndex(item => new { item.ResolvedAtUtc, item.RecordedAtUtc, item.Id })
                .HasDatabaseName("IX_FeedActivityEventQuarantine_Resolution");
            builder.HasIndex(item => new { item.ActorUserId, item.Id })
                .HasDatabaseName("IX_FeedActivityEventQuarantine_Actor_Id");
        }
    }
}
