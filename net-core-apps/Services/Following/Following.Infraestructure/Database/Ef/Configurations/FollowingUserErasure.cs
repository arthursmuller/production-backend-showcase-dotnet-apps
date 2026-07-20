using Following.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Following.Infra.EntityConfigurations
{
    public sealed class FollowingUserErasureConfiguration
        : IEntityTypeConfiguration<FollowingUserErasure>
    {
        public void Configure(EntityTypeBuilder<FollowingUserErasure> builder)
        {
            builder.ToTable("FollowingUserErasure", table =>
            {
                table.HasCheckConstraint("CK_FollowingUserErasure_User", "`UserId` > 0");
                table.HasCheckConstraint("CK_FollowingUserErasure_Version", "`ErasureVersion` > 0");
                table.HasCheckConstraint("CK_FollowingUserErasure_Status", "`Status` BETWEEN 0 AND 2");
                table.HasCheckConstraint("CK_FollowingUserErasure_Stage", "`Stage` BETWEEN 0 AND 14");
                table.HasCheckConstraint("CK_FollowingUserErasure_Cursor", "`Cursor` >= 0");
                table.HasCheckConstraint("CK_FollowingUserErasure_LeaseToken", "`LeaseToken` >= 0");
            });

            builder.HasKey(item => item.UserId);
            builder.Property(item => item.UserId).ValueGeneratedNever();
            builder.Property(item => item.RequestId)
                .HasColumnType("char(36)")
                .UseCollation("ascii_general_ci")
                .IsRequired();
            builder.Property(item => item.ErasureVersion).IsRequired();
            builder.Property(item => item.RequestedAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(item => item.Status)
                .HasConversion<byte>()
                .HasColumnType("tinyint unsigned")
                .IsRequired();
            builder.Property(item => item.Stage)
                .HasConversion<byte>()
                .HasColumnType("tinyint unsigned")
                .IsRequired();
            builder.Property(item => item.Cursor).IsRequired();
            builder.Property(item => item.LeaseOwner).HasMaxLength(128).IsUnicode(false);
            builder.Property(item => item.LeaseToken).IsRequired();
            builder.Property(item => item.LeaseExpiresAtUtc).HasColumnType("datetime(6)");
            builder.Property(item => item.AttemptCount).IsRequired();
            builder.Property(item => item.CreatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(item => item.UpdatedAtUtc).HasColumnType("datetime(6)").IsRequired();
            builder.Property(item => item.CompletedAtUtc).HasColumnType("datetime(6)");
            builder.Property(item => item.CompletionEventId)
                .HasColumnType("char(36)")
                .UseCollation("ascii_general_ci");
            builder.Property(item => item.RowVersion)
                .HasDefaultValue(1L)
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken()
                .IsRequired();

            builder.HasIndex(item => new
            {
                item.Status,
                item.LeaseExpiresAtUtc,
                item.UpdatedAtUtc,
                item.UserId,
            })
                .HasDatabaseName("IX_FollowingUserErasure_Claim");
        }
    }
}
