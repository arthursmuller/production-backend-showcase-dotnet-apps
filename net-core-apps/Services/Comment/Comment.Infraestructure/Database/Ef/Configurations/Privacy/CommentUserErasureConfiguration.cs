using Comment.Domain.AggregatesModel.Privacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comment.Infra.EntityConfigurations.Privacy;

public sealed class CommentUserErasureConfiguration
    : IEntityTypeConfiguration<CommentUserErasure>
{
    public void Configure(EntityTypeBuilder<CommentUserErasure> builder)
    {
        builder.ToTable("CommentUserErasure", table =>
        {
            table.HasCheckConstraint("CK_CmtUserErasure_User", "`UserId` > 0");
            table.HasCheckConstraint("CK_CmtUserErasure_Version",
                "`ErasureVersion` > 0 AND `FencingToken` >= 0 AND `Version` > 0");
            table.HasCheckConstraint("CK_CmtUserErasure_Status", "`Status` IN (1,2,3,4)");
            table.HasCheckConstraint("CK_CmtUserErasure_Stage", "`Stage` BETWEEN 1 AND 10");
            table.HasCheckConstraint("CK_CmtUserErasure_Lease",
                "(`Status` = 2 AND `LeaseOwner` IS NOT NULL AND `LeaseUntilUtc` IS NOT NULL) OR (`Status` <> 2 AND `LeaseOwner` IS NULL AND `LeaseUntilUtc` IS NULL)");
            table.HasCheckConstraint("CK_CmtUserErasure_Completion",
                "(`Status` = 4 AND `CompletedAtUtc` IS NOT NULL AND `CompletionEventId` IS NOT NULL) OR (`Status` <> 4 AND `CompletedAtUtc` IS NULL AND `CompletionEventId` IS NULL)");
        });
        builder.HasKey(value => value.UserId);
        builder.Property(value => value.UserId).ValueGeneratedNever();
        builder.Property(value => value.RequestId).IsRequired();
        builder.Property(value => value.RequestEventId).IsRequired();
        builder.Property(value => value.ErasureVersion).IsRequired();
        builder.Property(value => value.RequestedAtUtc).HasPrecision(6).IsRequired();
        builder.Property(value => value.Status).HasConversion<byte>().IsRequired();
        builder.Property(value => value.Stage).HasConversion<byte>().IsRequired();
        builder.Property(value => value.Cursor).IsRequired();
        builder.Property(value => value.LeaseOwner).HasMaxLength(128);
        builder.Property(value => value.LeaseUntilUtc).HasPrecision(6);
        builder.Property(value => value.LastFailureCode).HasMaxLength(64);
        builder.Property(value => value.LastFailureAtUtc).HasPrecision(6);
        builder.Property(value => value.CreatedAtUtc).HasPrecision(6).IsRequired();
        builder.Property(value => value.UpdatedAtUtc).HasPrecision(6).IsRequired();
        builder.Property(value => value.CompletedAtUtc).HasPrecision(6);
        builder.Property(value => value.Version).IsRequired().IsConcurrencyToken();
        builder.HasIndex(value => new { value.Status, value.LeaseUntilUtc, value.RequestedAtUtc, value.UserId })
            .HasDatabaseName("IX_CmtUserErasure_Work");
        builder.HasIndex(value => value.RequestId).IsUnique()
            .HasDatabaseName("UX_CmtUserErasure_Request");
    }
}

public sealed class CommentErasureMediaObjectConfiguration
    : IEntityTypeConfiguration<CommentErasureMediaObject>
{
    public void Configure(EntityTypeBuilder<CommentErasureMediaObject> builder)
    {
        builder.ToTable("CommentErasureMediaObject", table =>
        {
            table.HasCheckConstraint("CK_CmtEraseMedia_User", "`UserId` > 0 AND `IngestionId` > 0");
            table.HasCheckConstraint("CK_CmtEraseMedia_Kind", "`ObjectKind` IN ('final','quarantine')");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedOnAdd();
        builder.Property(value => value.ObjectKind).HasMaxLength(16).IsRequired();
        builder.Property(value => value.ObjectName).HasMaxLength(255).IsRequired();
        builder.Property(value => value.ObjectVersion).HasMaxLength(256).IsRequired();
        builder.Property(value => value.CreatedAtUtc).HasPrecision(6).IsRequired();
        builder.Property(value => value.LastAttemptAtUtc).HasPrecision(6);
        builder.Property(value => value.DeletedAtUtc).HasPrecision(6);
        builder.Property(value => value.LastFailureCode).HasMaxLength(64);
        builder.Property(value => value.Version).IsRequired().IsConcurrencyToken();
        builder.HasIndex(value => new { value.UserId, value.ObjectKind, value.ObjectName })
            .IsUnique().HasDatabaseName("UX_CmtEraseMedia_Owner_Object");
        builder.HasIndex(value => new { value.UserId, value.DeletedAtUtc, value.Id })
            .HasDatabaseName("IX_CmtEraseMedia_Work");
        builder.HasIndex(value => new { value.UserId, value.IngestionId })
            .HasDatabaseName("IX_CmtEraseMedia_Ingestion");
    }
}

public sealed class CommentUserMutationGuardConfiguration
    : IEntityTypeConfiguration<CommentUserMutationGuard>
{
    public void Configure(EntityTypeBuilder<CommentUserMutationGuard> builder)
    {
        builder.ToTable("CommentUserMutationGuard", table =>
            table.HasCheckConstraint("CK_CmtUserMutationGuard_User", "`UserId` > 0"));
        builder.HasKey(value => value.UserId);
        builder.Property(value => value.UserId).ValueGeneratedNever();
        builder.Property(value => value.CreatedAtUtc).HasPrecision(6).IsRequired();
    }
}
