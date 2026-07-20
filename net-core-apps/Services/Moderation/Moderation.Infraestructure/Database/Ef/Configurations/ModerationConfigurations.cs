using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Moderation.Domain.AggregatesModel;

namespace Moderation.Infra.Database.Ef.Configurations;

public sealed class ModerationReportConfiguration : IEntityTypeConfiguration<ModerationReport>
{
    public void Configure(EntityTypeBuilder<ModerationReport> builder)
    {
        builder.ToTable("ModerationReports", table =>
        {
            table.HasCheckConstraint("CK_ModerationReports_ResourceKind", "`ResourceKind` BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_ModerationReports_Reason", "`Reason` BETWEEN 1 AND 10");
            table.HasCheckConstraint("CK_ModerationReports_IdempotencyHash", "`IdempotencyKeyHash` REGEXP '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("CK_ModerationReports_RequestHash", "`RequestHash` REGEXP '^[0-9a-f]{64}$'");
        });
        builder.HasKey(report => report.Id);
        builder.Property(report => report.Id).ValueGeneratedNever();
        builder.Property(report => report.CaseId).IsRequired();
        builder.Property(report => report.ReporterId).HasMaxLength(128).UseCollation("utf8mb4_bin").IsRequired();
        builder.Property(report => report.ResourceKind).HasConversion<int>().IsRequired();
        builder.Property(report => report.ResourceId).HasMaxLength(128).UseCollation("utf8mb4_bin").IsRequired();
        builder.Property(report => report.Reason).HasConversion<int>().IsRequired();
        builder.Property(report => report.IdempotencyKeyHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(report => report.RequestHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(report => report.ReceivedAtUtc).HasPrecision(6).IsRequired();
        builder.HasIndex(report => new { report.ReporterId, report.IdempotencyKeyHash })
            .IsUnique()
            .HasDatabaseName("UX_ModerationReports_Reporter_Idempotency");
        builder.HasIndex(report => report.CaseId).IsUnique();
        builder.HasIndex(report => new { report.ResourceKind, report.ResourceId, report.ReceivedAtUtc });
        builder.HasMany(report => report.EvidenceReferences)
            .WithOne()
            .HasForeignKey(evidence => evidence.ReportId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(report => report.EvidenceReferences)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ModerationCaseMessageConfiguration : IEntityTypeConfiguration<ModerationCaseMessage>
{
    public void Configure(EntityTypeBuilder<ModerationCaseMessage> builder)
    {
        builder.ToTable("ModerationCaseMessages", table =>
        {
            table.HasCheckConstraint("CK_ModerationCaseMessages_Hash", "`Sha256` REGEXP '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("CK_ModerationCaseMessages_Version", "`CaseVersion` >= 2");
        });
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.CaseId).IsRequired();
        builder.Property(message => message.ActorId).HasMaxLength(128).UseCollation("utf8mb4_bin").IsRequired();
        builder.Property(message => message.Reference).HasMaxLength(256).UseCollation("utf8mb4_bin").IsRequired();
        builder.Property(message => message.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(message => message.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(message => message.CaseVersion).IsRequired();
        builder.Property(message => message.CreatedAtUtc).HasPrecision(6).IsRequired();
        builder.HasIndex(message => new { message.CaseId, message.CaseVersion }).IsUnique();
        builder.HasIndex(message => new { message.CaseId, message.CreatedAtUtc, message.Id });
        builder.HasOne<ModerationCase>()
            .WithMany()
            .HasForeignKey(message => message.CaseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EvidenceReferenceConfiguration : IEntityTypeConfiguration<EvidenceReference>
{
    public void Configure(EntityTypeBuilder<EvidenceReference> builder)
    {
        builder.ToTable("ModerationEvidenceReferences", table =>
        {
            table.HasCheckConstraint("CK_ModerationEvidenceReferences_Kind", "`Kind` BETWEEN 1 AND 3");
            table.HasCheckConstraint("CK_ModerationEvidenceReferences_Sha256", "`Sha256` REGEXP '^[0-9a-f]{64}$'");
        });
        builder.HasKey(evidence => evidence.Id);
        builder.Property(evidence => evidence.Id).ValueGeneratedNever();
        builder.Property(evidence => evidence.ReportId).IsRequired();
        builder.Property(evidence => evidence.Kind).HasConversion<int>().IsRequired();
        builder.Property(evidence => evidence.Reference).HasMaxLength(256).UseCollation("utf8mb4_bin").IsRequired();
        builder.Property(evidence => evidence.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.HasIndex(evidence => new { evidence.ReportId, evidence.Kind });
    }
}

public sealed class ModerationCaseConfiguration : IEntityTypeConfiguration<ModerationCase>
{
    public void Configure(EntityTypeBuilder<ModerationCase> builder)
    {
        builder.ToTable("ModerationCases", table =>
        {
            table.HasCheckConstraint("CK_ModerationCases_ResourceKind", "`ResourceKind` BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_ModerationCases_Status", "`Status` BETWEEN 1 AND 7");
            table.HasCheckConstraint("CK_ModerationCases_Priority", "`Priority` BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_ModerationCases_Versions", "`Version` >= 1 AND `DecisionVersion` >= 0");
            table.HasCheckConstraint("CK_ModerationCases_DecisionAction", "`DecisionAction` IS NULL OR `DecisionAction` BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_ModerationCases_Sla", "`SlaDueAtUtc` >= `ReceivedAtUtc`");
            table.HasCheckConstraint("CK_ModerationCases_RetentionClaim", "`RetentionClaimToken` >= 0");
            table.HasCheckConstraint(
                "CK_ModerationCases_ErasureClaim",
                "`ErasureClaimToken` >= 0 AND (`ErasureSubjectUserId` IS NULL OR `ErasureSubjectUserId` > 0)");
            table.HasCheckConstraint(
                "CK_ModerationCases_ErasureSubjectHash",
                "`ErasureSubjectHash` IS NULL OR `ErasureSubjectHash` REGEXP '^[0-9a-f]{64}$'");
        });
        builder.HasKey(moderationCase => moderationCase.Id);
        builder.Property(moderationCase => moderationCase.Id).ValueGeneratedNever();
        builder.Property(moderationCase => moderationCase.ReportId).IsRequired();
        builder.Property(moderationCase => moderationCase.ResourceKind).HasConversion<int>().IsRequired();
        builder.Property(moderationCase => moderationCase.ResourceId).HasMaxLength(128).UseCollation("utf8mb4_bin").IsRequired();
        builder.Property(moderationCase => moderationCase.ReporterId).HasMaxLength(128).UseCollation("utf8mb4_bin").IsRequired();
        builder.Property(moderationCase => moderationCase.Status).HasConversion<int>().IsRequired();
        builder.Property(moderationCase => moderationCase.Priority).HasConversion<int>().IsRequired();
        builder.Property(moderationCase => moderationCase.AssignedInvestigatorId).HasMaxLength(128);
        builder.Property(moderationCase => moderationCase.Version).IsConcurrencyToken().IsRequired();
        builder.Property(moderationCase => moderationCase.DecisionVersion).IsRequired();
        builder.Property(moderationCase => moderationCase.DecisionAction).HasConversion<int?>();
        builder.Property(moderationCase => moderationCase.ReceivedAtUtc).HasPrecision(6).IsRequired();
        builder.Property(moderationCase => moderationCase.LastChangedAtUtc).HasPrecision(6).IsRequired();
        builder.Property(moderationCase => moderationCase.SlaDueAtUtc).HasPrecision(6).IsRequired();
        builder.Property(moderationCase => moderationCase.EscalatedAtUtc).HasPrecision(6);
        builder.Property(moderationCase => moderationCase.DecidedAtUtc).HasPrecision(6);
        builder.Property(moderationCase => moderationCase.LastAuditHash).HasMaxLength(64).IsRequired();
        builder.Property(moderationCase => moderationCase.RetentionRedactedAtUtc).HasPrecision(6);
        builder.Property(moderationCase => moderationCase.RetentionClaimToken).HasDefaultValue(0L).IsRequired();
        builder.Property(moderationCase => moderationCase.ErasureSubjectHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .UseCollation("ascii_bin");
        builder.Property(moderationCase => moderationCase.ErasureSubjectUserId);
        builder.Property(moderationCase => moderationCase.ErasureClaimToken)
            .HasDefaultValue(0L)
            .IsRequired();
        builder.HasIndex(moderationCase => moderationCase.ReportId).IsUnique();
        builder.HasOne<ModerationReport>()
            .WithOne()
            .HasForeignKey<ModerationCase>(moderationCase => moderationCase.ReportId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(moderationCase => new
        {
            moderationCase.Status,
            moderationCase.EscalatedAtUtc,
            moderationCase.SlaDueAtUtc,
            moderationCase.Id,
        }).HasDatabaseName("IX_ModerationCases_SlaQueue");
        builder.HasIndex(moderationCase => new
        {
            moderationCase.ReceivedAtUtc,
            moderationCase.Id,
        }).HasDatabaseName("IX_ModerationCases_Cursor");
        builder.HasIndex(moderationCase => new
        {
            moderationCase.Status,
            moderationCase.RetentionRedactedAtUtc,
            moderationCase.LastChangedAtUtc,
            moderationCase.Id,
        }).HasDatabaseName("IX_ModerationCases_Retention");
        builder.HasIndex(moderationCase => new
        {
            moderationCase.ReporterId,
            moderationCase.ReceivedAtUtc,
            moderationCase.Id,
        }).HasDatabaseName("IX_ModerationCases_ReporterErasure");
        builder.HasIndex(moderationCase => new
        {
            moderationCase.ErasureSubjectHash,
            moderationCase.ErasureClaimToken,
            moderationCase.Id,
        }).HasDatabaseName("IX_ModerationCases_ErasureSubjectHash");
        builder.HasIndex(moderationCase => new
        {
            moderationCase.ErasureSubjectUserId,
            moderationCase.ErasureClaimToken,
            moderationCase.Id,
        }).HasDatabaseName("IX_ModerationCases_ErasureClaim");
        builder.HasMany(moderationCase => moderationCase.AuditEntries)
            .WithOne()
            .HasForeignKey(audit => audit.CaseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(moderationCase => moderationCase.AuditEntries)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ModerationDecisionStateConfiguration
    : IEntityTypeConfiguration<ModerationDecisionState>
{
    public void Configure(EntityTypeBuilder<ModerationDecisionState> builder)
    {
        builder.ToTable("ModerationDecisionStates", table =>
        {
            table.HasCheckConstraint(
                "CK_ModerationDecisionState_ResourceKind",
                "`ResourceKind` BETWEEN 1 AND 5");
            table.HasCheckConstraint(
                "CK_ModerationDecisionState_Action",
                "`Action` BETWEEN 1 AND 5");
            table.HasCheckConstraint(
                "CK_ModerationDecisionState_Versions",
                "`DecisionVersion` >= 1 AND `CaseVersion` >= `DecisionVersion`");
        });
        builder.HasKey(state => state.ExportCursor);
        builder.Property(state => state.ExportCursor).ValueGeneratedOnAdd();
        builder.Property(state => state.CaseId).ValueGeneratedNever().IsRequired();
        builder.Property(state => state.ResourceKind).HasConversion<int>().IsRequired();
        builder.Property(state => state.ResourceId)
            .HasMaxLength(128)
            .UseCollation("utf8mb4_bin")
            .IsRequired();
        builder.Property(state => state.Action).HasConversion<int>().IsRequired();
        builder.Property(state => state.DecisionVersion).IsConcurrencyToken().IsRequired();
        builder.Property(state => state.CaseVersion).IsRequired();
        builder.Property(state => state.DecidedAtUtc).HasPrecision(6).IsRequired();
        builder.HasIndex(state => state.CaseId)
            .IsUnique()
            .HasDatabaseName("UX_ModerationDecisionState_Case");
        builder.HasOne<ModerationCase>()
            .WithOne(moderationCase => moderationCase.DecisionState)
            .HasForeignKey<ModerationDecisionState>(state => state.CaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ModerationDecisionStateRevisionConfiguration
    : IEntityTypeConfiguration<ModerationDecisionStateRevision>
{
    public void Configure(EntityTypeBuilder<ModerationDecisionStateRevision> builder)
    {
        builder.ToTable("ModerationDecisionStateRevisions", table =>
        {
            table.HasCheckConstraint(
                "CK_ModerationDecisionRevision_ResourceKind",
                "`ResourceKind` BETWEEN 1 AND 5");
            table.HasCheckConstraint(
                "CK_ModerationDecisionRevision_Action",
                "`Action` BETWEEN 1 AND 5");
            table.HasCheckConstraint(
                "CK_ModerationDecisionRevision_Versions",
                "`DecisionVersion` >= 1 AND `CaseVersion` >= `DecisionVersion`");
            table.HasCheckConstraint(
                "CK_ModerationDecisionRevision_Hash",
                "CHAR_LENGTH(`RowSha256`) = 64");
        });
        builder.HasKey(revision => revision.RevisionCursor);
        builder.Property(revision => revision.RevisionCursor).ValueGeneratedOnAdd();
        builder.Property(revision => revision.CaseId).ValueGeneratedNever().IsRequired();
        builder.Property(revision => revision.ResourceKind).HasConversion<int>().IsRequired();
        builder.Property(revision => revision.ResourceId)
            .HasMaxLength(128)
            .UseCollation("utf8mb4_bin")
            .IsRequired();
        builder.Property(revision => revision.Action).HasConversion<int>().IsRequired();
        builder.Property(revision => revision.DecisionVersion).IsRequired();
        builder.Property(revision => revision.CaseVersion).IsRequired();
        builder.Property(revision => revision.DecidedAtUtc).HasPrecision(6).IsRequired();
        builder.Property(revision => revision.PartitionKey).HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(revision => revision.RowSha256)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .UseCollation("ascii_bin")
            .IsRequired();
        builder.Property(revision => revision.RecordedAtUtc)
            .HasPrecision(6)
            .HasDefaultValueSql("UTC_TIMESTAMP(6)")
            .IsRequired();
        builder.HasIndex(revision => new { revision.CaseId, revision.RevisionCursor })
            .HasDatabaseName("IX_ModerationDecisionRevision_Case_Cursor");
        builder.HasIndex(revision => new { revision.PartitionKey, revision.RevisionCursor })
            .HasDatabaseName("IX_ModerationDecisionRevision_Partition_Cursor");
        builder.HasIndex(revision => new { revision.RecordedAtUtc, revision.RevisionCursor })
            .HasDatabaseName("IX_ModerationDecisionRevision_Retention");
    }
}

public sealed class ModerationUserErasureConfiguration
    : IEntityTypeConfiguration<ModerationUserErasure>
{
    public void Configure(EntityTypeBuilder<ModerationUserErasure> builder)
    {
        builder.ToTable("ModerationUserErasures", table =>
        {
            table.HasCheckConstraint(
                "CK_ModerationUserErasures_User",
                "`UserId` > 0");
            table.HasCheckConstraint(
                "CK_ModerationUserErasures_Version",
                "`ErasureVersion` > 0 AND `FencingToken` >= 0");
            table.HasCheckConstraint(
                "CK_ModerationUserErasures_Stage",
                "`Stage` IN (1,2)");
            table.HasCheckConstraint(
                "CK_ModerationUserErasures_Lease",
                "(`LeaseOwner` IS NULL AND `LeaseUntilUtc` IS NULL) OR (`LeaseOwner` IS NOT NULL AND `LeaseUntilUtc` IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_ModerationUserErasures_HeldCases",
                "`HeldCaseCount` >= 0");
        });
        builder.HasKey(erasure => erasure.UserId);
        builder.Property(erasure => erasure.UserId).ValueGeneratedNever();
        builder.Property(erasure => erasure.RequestId)
            .HasColumnType("char(36)")
            .IsRequired();
        builder.Property(erasure => erasure.ErasureVersion).IsRequired();
        builder.Property(erasure => erasure.RequestedAtUtc).HasPrecision(6).IsRequired();
        builder.Property(erasure => erasure.Stage)
            .HasColumnType("tinyint unsigned")
            .IsRequired();
        builder.Property(erasure => erasure.NextAttemptAtUtc).HasPrecision(6);
        builder.Property(erasure => erasure.LeaseOwner)
            .HasMaxLength(64)
            .IsUnicode(false);
        builder.Property(erasure => erasure.LeaseUntilUtc).HasPrecision(6);
        builder.Property(erasure => erasure.FencingToken).IsRequired();
        builder.Property(erasure => erasure.LastProcessedAtUtc).HasPrecision(6);
        builder.Property(erasure => erasure.CompletedAtUtc).HasPrecision(6);
        builder.Property(erasure => erasure.HeldCaseCount).IsRequired();
        builder.HasIndex(erasure => erasure.RequestId)
            .IsUnique()
            .HasDatabaseName("UX_ModerationUserErasures_Request");
        builder.HasIndex(erasure => new
        {
            erasure.Stage,
            erasure.NextAttemptAtUtc,
            erasure.LeaseUntilUtc,
            erasure.LastProcessedAtUtc,
            erasure.UserId,
        }).HasDatabaseName("IX_ModerationUserErasures_Work");
    }
}

public sealed class ModerationRetentionCheckpointConfiguration
    : IEntityTypeConfiguration<ModerationRetentionCheckpoint>
{
    public void Configure(EntityTypeBuilder<ModerationRetentionCheckpoint> builder)
    {
        builder.ToTable("ModerationRetentionCheckpoints", table =>
        {
            table.HasCheckConstraint(
                "CK_ModerationRetentionCheckpoints_Singleton",
                "`Id` = 1");
            table.HasCheckConstraint(
                "CK_ModerationRetentionCheckpoints_FencingToken",
                "`FencingToken` >= 0");
            table.HasCheckConstraint(
                "CK_ModerationRetentionCheckpoints_Totals",
                "`TotalCasesRedacted` >= 0 AND `TotalCasesPurged` >= 0");
        });
        builder.HasKey(checkpoint => checkpoint.Id);
        builder.Property(checkpoint => checkpoint.Id).ValueGeneratedNever();
        builder.Property(checkpoint => checkpoint.LeaseOwner).HasMaxLength(64);
        builder.Property(checkpoint => checkpoint.LeaseUntilUtc).HasPrecision(6);
        builder.Property(checkpoint => checkpoint.FencingToken).IsRequired();
        builder.Property(checkpoint => checkpoint.LastStartedAtUtc).HasPrecision(6);
        builder.Property(checkpoint => checkpoint.LastCompletedAtUtc).HasPrecision(6);
        builder.Property(checkpoint => checkpoint.TotalCasesRedacted).IsRequired();
        builder.Property(checkpoint => checkpoint.TotalCasesPurged).IsRequired();
    }
}

public sealed class ModerationAuditEntryConfiguration : IEntityTypeConfiguration<ModerationAuditEntry>
{
    public void Configure(EntityTypeBuilder<ModerationAuditEntry> builder)
    {
        builder.ToTable("ModerationAuditEntries", table =>
        {
            table.HasCheckConstraint("CK_ModerationAuditEntries_Version", "`CaseVersion` >= 1");
            table.HasCheckConstraint("CK_ModerationAuditEntries_Hash", "`Hash` REGEXP '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("CK_ModerationAuditEntries_PreviousHash", "`PreviousHash` = '' OR `PreviousHash` REGEXP '^[0-9a-f]{64}$'");
        });
        builder.HasKey(audit => audit.Id);
        builder.Property(audit => audit.Id).ValueGeneratedNever();
        builder.Property(audit => audit.CaseId).IsRequired();
        builder.Property(audit => audit.ActorId).HasMaxLength(128).IsRequired();
        builder.Property(audit => audit.Action).HasMaxLength(64).IsRequired();
        builder.Property(audit => audit.ReasonCode).HasMaxLength(64).IsRequired();
        builder.Property(audit => audit.CaseVersion).IsRequired();
        builder.Property(audit => audit.PreviousHash).HasMaxLength(64).IsRequired();
        builder.Property(audit => audit.Hash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(audit => audit.OccurredAtUtc).HasPrecision(6).IsRequired();
        builder.HasIndex(audit => new { audit.CaseId, audit.CaseVersion }).IsUnique();
        builder.HasIndex(audit => audit.OccurredAtUtc);
    }
}

public sealed class LegalHoldConfiguration : IEntityTypeConfiguration<LegalHold>
{
    public void Configure(EntityTypeBuilder<LegalHold> builder)
    {
        builder.ToTable("ModerationLegalHolds", table =>
        {
            table.HasCheckConstraint("CK_ModerationLegalHolds_ResourceKind", "`ResourceKind` BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_ModerationLegalHolds_Release", "(`IsActive` = 1 AND `ReleasedAtUtc` IS NULL AND `ActiveCaseKey` IS NOT NULL) OR (`IsActive` = 0 AND `ReleasedAtUtc` IS NOT NULL AND `ActiveCaseKey` IS NULL)");
        });
        builder.HasKey(hold => hold.Id);
        builder.Property(hold => hold.Id).ValueGeneratedNever();
        builder.Property(hold => hold.CaseId).IsRequired();
        builder.Property(hold => hold.ResourceKind).HasConversion<int>().IsRequired();
        builder.Property(hold => hold.ResourceId).HasMaxLength(128).UseCollation("utf8mb4_bin").IsRequired();
        builder.Property(hold => hold.PlacedByActorId).HasMaxLength(128).IsRequired();
        builder.Property(hold => hold.ReasonCode).HasMaxLength(64).IsRequired();
        builder.Property(hold => hold.PlacedAtUtc).HasPrecision(6).IsRequired();
        builder.Property(hold => hold.IsActive).IsRequired();
        builder.Property(hold => hold.ReleasedByActorId).HasMaxLength(128);
        builder.Property(hold => hold.ReleaseReasonCode).HasMaxLength(64);
        builder.Property(hold => hold.ReleasedAtUtc).HasPrecision(6);
        builder.Property(hold => hold.ActiveCaseKey).HasMaxLength(36);
        builder.HasIndex(hold => hold.ActiveCaseKey)
            .IsUnique()
            .HasDatabaseName("UX_ModerationLegalHolds_ActiveCase");
        builder.HasIndex(hold => new { hold.CaseId, hold.PlacedAtUtc });
        builder.HasOne<ModerationCase>()
            .WithMany()
            .HasForeignKey(hold => hold.CaseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
