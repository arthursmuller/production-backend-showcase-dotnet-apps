using Following.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Following.Infra.EntityConfigurations
{
    public sealed class UserBlockConfiguration
        : BaseConfiguration, IEntityTypeConfiguration<UserBlock>
    {
        public void Configure(EntityTypeBuilder<UserBlock> builder)
        {
            ConfigureBase(builder, "UserBlock");

            builder.Property(item => item.BlockerUserId).IsRequired();
            builder.Property(item => item.BlockedUserId).IsRequired();

            builder.HasIndex(item => new { item.BlockerUserId, item.BlockedUserId })
                .IsUnique()
                .HasDatabaseName("UX_UserBlock_Direction");
            builder.HasIndex(item => new { item.BlockedUserId, item.BlockerUserId })
                .HasDatabaseName("IX_UserBlock_ReverseDirection");
            builder.HasIndex(item => new { item.BlockerUserId, item.Id })
                .HasDatabaseName("IX_UserBlock_Erase_Blocker");
            builder.HasIndex(item => new { item.BlockedUserId, item.Id })
                .HasDatabaseName("IX_UserBlock_Erase_Blocked");
            builder.HasIndex(item => new { item.BlockerUserId, item.CreatedDate, item.Id })
                .IsDescending(false, true, true)
                .HasDatabaseName("IX_UserBlock_Blocker_Cursor");

            builder.ToTable("UserBlock", table =>
            {
                table.HasCheckConstraint(
                    "CK_UserBlock_PositiveUsers",
                    "`BlockerUserId` > 0 AND `BlockedUserId` > 0");
                table.HasCheckConstraint(
                    "CK_UserBlock_NoSelf",
                    "`BlockerUserId` <> `BlockedUserId`");
            });
        }
    }
}
