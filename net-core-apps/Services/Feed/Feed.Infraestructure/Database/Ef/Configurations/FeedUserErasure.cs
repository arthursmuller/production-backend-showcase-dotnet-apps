using Feed.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Feed.Infra.EntityConfigurations;

public sealed class FeedUserErasureConfiguration : IEntityTypeConfiguration<FeedUserErasure>
{
    public void Configure(EntityTypeBuilder<FeedUserErasure> builder)
    {
        builder.ToTable("FeedUserErasure", table =>
        {
            table.HasCheckConstraint("CK_FeedUserErasure_User", "`UserId` > 0");
            table.HasCheckConstraint("CK_FeedUserErasure_ErasureVersion", "`ErasureVersion` > 0");
            table.HasCheckConstraint("CK_FeedUserErasure_Status", "`Status` BETWEEN 0 AND 2");
            table.HasCheckConstraint("CK_FeedUserErasure_Stage", "`Stage` BETWEEN 0 AND 20");
            table.HasCheckConstraint("CK_FeedUserErasure_Cursors", "`Cursor` >= 0 AND `SecondaryCursor` >= 0");
            table.HasCheckConstraint("CK_FeedUserErasure_LeaseToken", "`LeaseToken` >= 0");
        });
        builder.HasKey(item => item.UserId);
        builder.Property(item => item.UserId).ValueGeneratedNever();
        builder.Property(item => item.RequestId).HasColumnType("char(36)").UseCollation("ascii_general_ci").IsRequired();
        builder.Property(item => item.RequestEventId).HasColumnType("char(36)").UseCollation("ascii_general_ci").IsRequired();
        builder.Property(item => item.ErasureVersion).IsRequired();
        builder.Property(item => item.RequestedAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.Property(item => item.Status).HasConversion<byte>().HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(item => item.Stage).HasConversion<byte>().HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(item => item.Cursor).IsRequired();
        builder.Property(item => item.SecondaryCursor).IsRequired();
        builder.Property(item => item.LeaseOwner).HasMaxLength(128).IsUnicode(false);
        builder.Property(item => item.LeaseToken).IsRequired();
        builder.Property(item => item.LeaseExpiresAtUtc).HasColumnType("datetime(6)");
        builder.Property(item => item.AttemptCount).IsRequired();
        builder.Property(item => item.CacheSafeAfterUtc).HasColumnType("datetime(6)");
        builder.Property(item => item.CreatedAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.Property(item => item.UpdatedAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.Property(item => item.CompletedAtUtc).HasColumnType("datetime(6)");
        builder.Property(item => item.CompletionEventId).HasColumnType("char(36)").UseCollation("ascii_general_ci");
        builder.Property(item => item.RowVersion)
            .HasDefaultValue(1L)
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken()
            .IsRequired();
        builder.HasIndex(item => new
        {
            item.Status,
            item.LeaseExpiresAtUtc,
            item.RequestedAtUtc,
            item.UserId,
        })
            .HasDatabaseName("IX_FeedUserErasure_Claim");
    }
}

public sealed class FeedUserMutationGuardConfiguration : IEntityTypeConfiguration<FeedUserMutationGuard>
{
    public void Configure(EntityTypeBuilder<FeedUserMutationGuard> builder)
    {
        builder.ToTable("FeedUserMutationGuard", table =>
            table.HasCheckConstraint("CK_FeedUserMutationGuard_User", "`UserId` > 0"));
        builder.HasKey(item => item.UserId);
        builder.Property(item => item.UserId).ValueGeneratedNever();
        builder.Property(item => item.CreatedAtUtc).HasColumnType("datetime(6)").IsRequired();
    }
}
