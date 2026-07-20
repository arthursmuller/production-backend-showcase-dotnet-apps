using Comment.Domain.AggregatesModel.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comment.Infra.EntityConfigurations.Media;

public sealed class CommentMediaIngestionConfiguration
    : BaseConfiguration, IEntityTypeConfiguration<CommentMediaIngestion>
{
    public void Configure(EntityTypeBuilder<CommentMediaIngestion> builder)
    {
        ConfigureBaseEntity(builder, "CommentMediaIngestion");
        builder.Property(value => value.UploadIdentity).IsRequired().HasMaxLength(64);
        builder.Property(value => value.RequestFingerprint).IsRequired().HasMaxLength(64);
        builder.Property(value => value.OwnerUserId).IsRequired();
        builder.Property(value => value.Operation).IsRequired().HasMaxLength(32);
        builder.Property(value => value.TargetCommentId);
        builder.Property(value => value.BoundCommentId);
        builder.Property(value => value.State).IsRequired().HasConversion<int>();
        builder.Property(value => value.Attempts).IsRequired();
        builder.Property(value => value.NextAttemptAtUtc);
        builder.Property(value => value.ClaimedAtUtc);
        builder.Property(value => value.LeaseUntilUtc);
        builder.Property(value => value.LeaseOwner).HasMaxLength(128);
        builder.Property(value => value.LastFailureCode).HasMaxLength(64);
        builder.Property(value => value.CompletedAtUtc);
        builder.Property(value => value.Version).IsRequired().IsConcurrencyToken();

        builder.HasIndex(value => value.UploadIdentity)
            .IsUnique()
            .HasDatabaseName("UX_CmtMediaIngestion_UploadIdentity");
        builder.HasIndex(value => new
        {
            value.State,
            value.NextAttemptAtUtc,
            value.LeaseUntilUtc,
            value.Id,
        })
            .HasDatabaseName("IX_CmtMediaIngestion_Due");
        builder.HasIndex(value => new { value.OwnerUserId, value.BoundCommentId })
            .HasDatabaseName("IX_CmtMediaIngestion_Owner_BoundComment");

        builder.OwnsMany(value => value.Items, items =>
        {
            items.ToTable("CommentMediaIngestionItem");
            items.WithOwner()
                .HasForeignKey("CommentMediaIngestionId")
                .HasConstraintName("FK_CmtMediaIngestionItem_Batch");
            items.HasKey("CommentMediaIngestionId", nameof(CommentMediaIngestionItem.Order))
                .HasName("PK_CommentMediaIngestionItem");
            items.Property(value => value.Order).IsRequired().ValueGeneratedNever();
            items.Property(value => value.OriginalFileName).IsRequired().HasMaxLength(255);
            items.Property(value => value.DeclaredMimeType).IsRequired().HasMaxLength(100);
            items.Property(value => value.DeclaredSizeBytes).IsRequired();
            items.Property(value => value.MediaType).IsRequired().HasConversion<int>();
            items.Property(value => value.QuarantineObjectName).IsRequired().HasMaxLength(128);
            items.Property(value => value.QuarantineObjectVersion).HasMaxLength(256);
            items.Property(value => value.QuarantinedAtUtc);
            items.Property(value => value.QuarantineDeletedAtUtc);
            items.Property(value => value.CanonicalFileName).HasMaxLength(255);
            items.Property(value => value.VerifiedMimeType).HasMaxLength(100);
            items.Property(value => value.CanonicalExtension).HasMaxLength(11);
            items.Property(value => value.VerifiedSizeBytes);
            items.Property(value => value.Sha256).HasMaxLength(64);
            items.Property(value => value.Scanner).HasMaxLength(64);
            items.Property(value => value.ScannerVersion).HasMaxLength(128);
            items.Property(value => value.SignatureVersion).HasMaxLength(128);
            items.Property(value => value.ScannedAtUtc);
            items.Property(value => value.FinalObjectName).HasMaxLength(128);
            items.Property(value => value.FinalObjectVersion).HasMaxLength(256);
            items.Property(value => value.PrivateObjectUrl).HasMaxLength(2048);
            items.Property(value => value.PromotedAtUtc);
            items.Property(value => value.BoundAtUtc);
            items.Property(value => value.CleanedAtUtc);
            items.Property(value => value.State).IsRequired().HasConversion<int>();
        });
        builder.Navigation(value => value.Items)
            .UsePropertyAccessMode(Microsoft.EntityFrameworkCore.PropertyAccessMode.Field);
    }
}
