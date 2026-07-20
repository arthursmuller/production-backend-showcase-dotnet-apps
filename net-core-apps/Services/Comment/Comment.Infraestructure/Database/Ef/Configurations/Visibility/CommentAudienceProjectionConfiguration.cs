using Comment.Domain.AggregatesModel.Visibility;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comment.Infra.EntityConfigurations.Visibility
{
    public sealed class CommentRelationshipCheckpointConfiguration
        : IEntityTypeConfiguration<CommentRelationshipCheckpoint>
    {
        public void Configure(EntityTypeBuilder<CommentRelationshipCheckpoint> builder)
        {
            builder.ToTable("CommentRelationshipCheckpoint");
            builder.HasKey(item => item.StreamId);
            builder.Property(item => item.RelationshipKind).HasMaxLength(32).IsUnicode(false).IsRequired();
            builder.Property(item => item.Status).HasMaxLength(24).IsUnicode(false).IsRequired();
            builder.Property(item => item.Version).IsConcurrencyToken().IsRequired();
            builder.HasIndex(item => new { item.OwnerUserId, item.ViewerUserId, item.RelationshipKind })
                .IsUnique().HasDatabaseName("UX_CmtRelCheckpoint_Identity");
            builder.HasIndex(item => new { item.Status, item.LastConfirmedAtUtc })
                .HasDatabaseName("IX_CmtRelCheckpoint_Health");
        }
    }

    public sealed class CommentRelationshipEdgeConfiguration
        : IEntityTypeConfiguration<CommentRelationshipEdge>
    {
        public void Configure(EntityTypeBuilder<CommentRelationshipEdge> builder)
        {
            builder.ToTable("CommentRelationshipEdge");
            builder.HasKey(item => item.Id);
            builder.Property(item => item.Id).ValueGeneratedOnAdd();
            builder.Property(item => item.RelationshipKind).HasMaxLength(32).IsUnicode(false).IsRequired();
            builder.Property(item => item.State).HasMaxLength(24).IsUnicode(false).IsRequired();
            builder.Property(item => item.Version).IsConcurrencyToken().IsRequired();
            builder.HasIndex(item => new { item.OwnerUserId, item.ViewerUserId, item.RelationshipKind })
                .IsUnique().HasDatabaseName("UX_CmtRelEdge_Identity");
            builder.HasIndex(item => new { item.OwnerUserId, item.RelationshipKind, item.State, item.ViewerUserId })
                .HasDatabaseName("IX_CmtRelEdge_Audience");
            builder.HasIndex(item => new { item.ViewerUserId, item.RelationshipKind, item.State, item.OwnerUserId })
                .HasDatabaseName("IX_CmtRelEdge_ReverseAudience");
        }
    }

    public sealed class CommentRelationshipEventQuarantineConfiguration
        : IEntityTypeConfiguration<CommentRelationshipEventQuarantine>
    {
        public void Configure(EntityTypeBuilder<CommentRelationshipEventQuarantine> builder)
        {
            builder.ToTable("CommentRelationshipEventQuarantine");
            builder.HasKey(item => item.Id);
            builder.Property(item => item.Id).ValueGeneratedOnAdd();
            builder.Property(item => item.RelationshipKind).HasMaxLength(32).IsUnicode(false).IsRequired();
            builder.Property(item => item.State).HasMaxLength(24).IsUnicode(false).IsRequired();
            builder.Property(item => item.ReasonCode).HasMaxLength(64).IsUnicode(false).IsRequired();
            builder.Property(item => item.Resolution).HasMaxLength(64).IsUnicode(false);
            builder.HasIndex(item => item.EventId).IsUnique().HasDatabaseName("UX_CmtRelQuarantine_EventId");
            builder.HasIndex(item => new { item.StreamId, item.ResolvedAtUtc })
                .HasDatabaseName("IX_CmtRelQuarantine_StreamResolution");
        }
    }

    public sealed class CommentAudienceProjectionStateConfiguration
        : IEntityTypeConfiguration<CommentAudienceProjectionState>
    {
        public void Configure(EntityTypeBuilder<CommentAudienceProjectionState> builder)
        {
            builder.ToTable("CommentAudienceProjectionState", table => table.HasCheckConstraint(
                "CK_CmtAudienceProjection_Singleton", "`Id` = 1"));
            builder.HasKey(item => item.Id);
            builder.Property(item => item.Id).ValueGeneratedNever();
            builder.Property(item => item.Status).HasMaxLength(24).IsUnicode(false).IsRequired();
            builder.Property(item => item.LastErrorCode).HasMaxLength(64).IsUnicode(false);
            builder.Property(item => item.LeaseOwner).HasMaxLength(128).IsUnicode(false);
            builder.Property(item => item.Version).IsConcurrencyToken().IsRequired();
            builder.HasIndex(item => new { item.Status, item.LastCompletedAtUtc })
                .HasDatabaseName("IX_CmtAudienceProjection_Health");
        }
    }
}
