using Following.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Following.Infra.EntityConfigurations
{
    public sealed class RelationshipEventStreamConfiguration
        : IEntityTypeConfiguration<RelationshipEventStream>
    {
        public void Configure(EntityTypeBuilder<RelationshipEventStream> builder)
        {
            builder.ToTable("RelationshipEventStream", table =>
            {
                table.HasCheckConstraint("CK_RelationshipEventStream_Owner", "`OwnerUserId` > 0");
                table.HasCheckConstraint("CK_RelationshipEventStream_Viewer", "`ViewerUserId` > 0");
                table.HasCheckConstraint(
                    "CK_RelationshipEventStream_Directional",
                    "`OwnerUserId` <> `ViewerUserId`");
                table.HasCheckConstraint(
                    "CK_RelationshipEventStream_CurrentVersion",
                    "`CurrentVersion` > 0");
                table.HasCheckConstraint("CK_RelationshipEventStream_Version", "`Version` > 0");
            });

            builder.HasKey(stream => stream.Id);
            builder.Property(stream => stream.Id).ValueGeneratedOnAdd();
            builder.Property(stream => stream.StreamId)
                .HasColumnType("char(36)")
                .UseCollation("ascii_general_ci")
                .IsRequired();
            builder.Property(stream => stream.OwnerUserId).IsRequired();
            builder.Property(stream => stream.ViewerUserId).IsRequired();
            builder.Property(stream => stream.RelationshipKind).HasMaxLength(32).IsRequired();
            builder.Property(stream => stream.CurrentState).HasMaxLength(24).IsRequired();
            builder.Property(stream => stream.CurrentVersion).IsRequired();
            builder.Property(stream => stream.LastEventId)
                .HasColumnType("char(36)")
                .UseCollation("ascii_general_ci")
                .IsRequired();
            builder.Property(stream => stream.LastOccurredAtUtc)
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(stream => stream.CreatedAtUtc)
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(stream => stream.UpdatedAtUtc)
                .HasColumnType("datetime(6)")
                .IsRequired();
            builder.Property(stream => stream.Version)
                .HasDefaultValue(1L)
                .IsConcurrencyToken()
                .IsRequired();

            builder.HasIndex(stream => stream.StreamId)
                .IsUnique()
                .HasDatabaseName("UX_RelationshipEventStream_StreamId");
            builder.HasIndex(stream => new
            {
                stream.OwnerUserId,
                stream.ViewerUserId,
                stream.RelationshipKind,
            })
                .IsUnique()
                .HasDatabaseName("UX_RelationshipEventStream_Direction_Kind");
            builder.HasIndex(stream => new { stream.UpdatedAtUtc, stream.Id })
                .HasDatabaseName("IX_RelationshipEventStream_Updated_Cursor");
            builder.HasIndex(stream => new { stream.OwnerUserId, stream.Id })
                .HasDatabaseName("IX_RelStream_Erase_Owner");
            builder.HasIndex(stream => new { stream.ViewerUserId, stream.Id })
                .HasDatabaseName("IX_RelStream_Erase_Viewer");
        }
    }
}
