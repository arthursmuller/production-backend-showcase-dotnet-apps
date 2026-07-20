using Interaction.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Interaction.Infraestructure.Database.Ef.Configurations;

internal static class InteractionConfiguration
{
    internal static PropertyBuilder<ResourceType> ResourceType<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class => builder.Property<ResourceType>(nameof(ResourceState.ResourceType)).HasColumnType("tinyint unsigned").IsRequired();

    internal static PropertyBuilder<string> ResourceId<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class => builder.Property<string>(nameof(ResourceState.ResourceId)).HasMaxLength(ResourceRef.MaximumIdLength).IsUnicode(false).UseCollation("utf8mb4_bin").IsRequired();
}

internal sealed class InteractionAuthorityCoordinateConfiguration
    : IEntityTypeConfiguration<InteractionAuthorityCoordinate>
{
    public void Configure(EntityTypeBuilder<InteractionAuthorityCoordinate> builder)
    {
        builder.ToTable("InteractionAuthorityCoordinates");
        builder.HasKey(item => item.Cursor);
        builder.Property(item => item.Cursor).ValueGeneratedOnAdd();
        InteractionConfiguration.ResourceType(builder);
        InteractionConfiguration.ResourceId(builder);
        builder.Property(item => item.CreatedAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.HasIndex(item => new { item.ResourceType, item.ResourceId })
            .IsUnique()
            .HasDatabaseName("UX_InteractionAuthorityCoordinate_Resource");
    }
}

internal sealed class InteractionAuthoritativeStateRevisionConfiguration
    : IEntityTypeConfiguration<InteractionAuthoritativeStateRevision>
{
    public void Configure(EntityTypeBuilder<InteractionAuthoritativeStateRevision> builder)
    {
        builder.ToTable("InteractionAuthoritativeStateRevisions", table =>
        {
            table.HasCheckConstraint("CK_InteractionAuthorityRevision_Partition", "`PartitionKey` BETWEEN 0 AND 255");
            table.HasCheckConstraint("CK_InteractionAuthorityRevision_Hash", "CHAR_LENGTH(`RowSha256`) = 64");
        });
        builder.HasKey(item => item.RevisionCursor);
        builder.Property(item => item.RevisionCursor).ValueGeneratedOnAdd();
        InteractionConfiguration.ResourceType(builder);
        InteractionConfiguration.ResourceId(builder);
        builder.Property(item => item.Lifecycle).HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(item => item.Visibility).HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(item => item.AppliedResourceVersion).IsRequired();
        builder.Property(item => item.HighestSeenResourceVersion).IsRequired();
        builder.Property(item => item.ResourceHasGap).IsRequired();
        builder.Property(item => item.ResourceConcurrencyVersion).IsRequired();
        builder.Property(item => item.ResourceUpdatedAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.Property(item => item.SummaryUpdatedAtUtc).HasColumnType("datetime(6)");
        builder.Property(item => item.PartitionKey).HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(item => item.RowSha256)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .UseCollation("ascii_bin")
            .IsRequired();
        builder.Property(item => item.RecordedAtUtc)
            .HasColumnType("datetime(6)")
            .HasDefaultValueSql("UTC_TIMESTAMP(6)")
            .IsRequired();
        builder.HasIndex(item => new { item.ResourceType, item.ResourceId, item.RevisionCursor })
            .HasDatabaseName("IX_InteractionAuthorityRevision_Resource_Cursor");
        builder.HasIndex(item => new { item.PartitionKey, item.RevisionCursor })
            .HasDatabaseName("IX_InteractionAuthorityRevision_Partition_Cursor");
        builder.HasIndex(item => new { item.RecordedAtUtc, item.RevisionCursor })
            .HasDatabaseName("IX_InteractionAuthorityRevision_Retention");
    }
}

internal sealed class ResourceStateConfiguration : IEntityTypeConfiguration<ResourceState>
{
    public void Configure(EntityTypeBuilder<ResourceState> builder)
    {
        builder.ToTable("ResourceStates", table =>
        {
            table.HasCheckConstraint("CK_ResourceState_Type", "`ResourceType` IN (1,2)");
            table.HasCheckConstraint("CK_ResourceState_Versions", "`AppliedVersion` >= 0 AND `HighestSeenVersion` >= `AppliedVersion`");
        });
        builder.HasKey(x => new { x.ResourceType, x.ResourceId });
        InteractionConfiguration.ResourceType(builder);
        InteractionConfiguration.ResourceId(builder);
        builder.Property(x => x.Lifecycle).HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(x => x.Visibility).HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(x => x.AppliedVersion).IsRequired();
        builder.Property(x => x.HighestSeenVersion).IsRequired();
        builder.Property(x => x.HasGap).IsRequired();
        builder.Property(x => x.ConcurrencyVersion).IsConcurrencyToken().IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.Ignore(x => x.Resource);
        builder.Ignore(x => x.IsPubliclyInteractable);
        builder.Ignore(x => x.IsActive);
        builder.Ignore(x => x.IsDeleted);
        builder.HasIndex(x => new { x.HasGap, x.UpdatedAtUtc }).HasDatabaseName("IX_ResourceState_Gap_Updated");
    }
}

internal sealed class ResourceStateGapConfiguration : IEntityTypeConfiguration<ResourceStateGap>
{
    public void Configure(EntityTypeBuilder<ResourceStateGap> builder)
    {
        builder.ToTable("ResourceStateGaps");
        builder.HasKey(x => new { x.ResourceType, x.ResourceId, x.SourceVersion });
        InteractionConfiguration.ResourceType(builder);
        InteractionConfiguration.ResourceId(builder);
        builder.Property(x => x.SourceVersion).IsRequired();
        builder.Property(x => x.Lifecycle).HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(x => x.Visibility).HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(x => x.OccurredAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.Property(x => x.ReceivedAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.HasOne<ResourceState>()
            .WithMany()
            .HasForeignKey(x => new { x.ResourceType, x.ResourceId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.ReceivedAtUtc).HasDatabaseName("IX_ResourceStateGap_Received");
    }
}

internal sealed class ReactionConfiguration : IEntityTypeConfiguration<Reaction>
{
    public void Configure(EntityTypeBuilder<Reaction> builder)
    {
        builder.ToTable("Reactions", table => table.HasCheckConstraint("CK_Reaction_Kind", "`Kind` IN (1,2)"));
        builder.HasKey(x => new { x.ResourceType, x.ResourceId, x.ActorId });
        InteractionConfiguration.ResourceType(builder);
        InteractionConfiguration.ResourceId(builder);
        builder.Property(x => x.ActorId).IsRequired();
        builder.Property(x => x.Kind).HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken().IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.HasOne<ResourceState>()
            .WithMany()
            .HasForeignKey(x => new { x.ResourceType, x.ResourceId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.ResourceType, x.ResourceId, x.Kind }).HasDatabaseName("IX_Reaction_Resource_Kind");
        builder.HasIndex(x => new { x.ActorId, x.UpdatedAtUtc }).HasDatabaseName("IX_Reaction_Actor_Updated");
    }
}

internal sealed class RatingConfiguration : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> builder)
    {
        builder.ToTable("Ratings", table => table.HasCheckConstraint("CK_Rating_Score", "`Score` BETWEEN 1 AND 6"));
        builder.HasKey(x => new { x.ResourceType, x.ResourceId, x.ActorId });
        InteractionConfiguration.ResourceType(builder);
        InteractionConfiguration.ResourceId(builder);
        builder.Property(x => x.ActorId).IsRequired();
        builder.Property(x => x.Score).HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(x => x.Comment).HasMaxLength(Rating.MaximumCommentLength);
        builder.Property(x => x.Version).IsConcurrencyToken().IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.HasOne<ResourceState>()
            .WithMany()
            .HasForeignKey(x => new { x.ResourceType, x.ResourceId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.ActorId, x.UpdatedAtUtc }).HasDatabaseName("IX_Rating_Actor_Updated");
    }
}

internal sealed class ViewObservationConfiguration : IEntityTypeConfiguration<ViewObservation>
{
    public void Configure(EntityTypeBuilder<ViewObservation> builder)
    {
        builder.ToTable("ViewObservations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        InteractionConfiguration.ResourceType(builder);
        InteractionConfiguration.ResourceId(builder);
        builder.Property(x => x.ObserverHash).HasColumnType("char(64)").IsUnicode(false).UseCollation("ascii_bin").IsRequired();
        builder.Property(x => x.BucketStartUtc).HasColumnType("datetime(6)").IsRequired();
        builder.Property(x => x.ReceivedAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.Property(x => x.ExpiresAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.Property(x => x.ProcessedAtUtc).HasColumnType("datetime(6)");
        builder.HasOne<ResourceState>()
            .WithMany()
            .HasForeignKey(x => new { x.ResourceType, x.ResourceId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.ResourceType, x.ResourceId, x.ObserverHash, x.BucketStartUtc })
            .IsUnique().HasDatabaseName("UX_ViewObservation_Dedupe");
        builder.HasIndex(x => new { x.ProcessedAtUtc, x.Id }).HasDatabaseName("IX_ViewObservation_Pending");
        builder.HasIndex(x => x.ExpiresAtUtc).HasDatabaseName("IX_ViewObservation_Expiry");
    }
}

internal sealed class EngagementSummaryConfiguration : IEntityTypeConfiguration<EngagementSummary>
{
    public void Configure(EntityTypeBuilder<EngagementSummary> builder)
    {
        builder.ToTable("EngagementSummaries", table =>
        {
            table.HasCheckConstraint("CK_EngagementSummary_Nonnegative",
                "`LikeCount` >= 0 AND `DislikeCount` >= 0 AND `RatingCount` >= 0 AND `RatingTotal` >= 0 AND `ViewCount` >= 0 AND `CommentCount` >= 0 AND `ShareCount` >= 0");
            table.HasCheckConstraint("CK_EngagementSummary_RatingTotal", "`RatingTotal` <= `RatingCount` * 6");
            table.HasCheckConstraint("CK_EngagementSummary_ContributionVersions", "`ContributionSourceVersion` >= 0 AND `ContributionHighestSeenVersion` >= `ContributionSourceVersion`");
        });
        builder.HasKey(x => new { x.ResourceType, x.ResourceId });
        InteractionConfiguration.ResourceType(builder);
        InteractionConfiguration.ResourceId(builder);
        builder.Property(x => x.LikeCount).IsRequired();
        builder.Property(x => x.DislikeCount).IsRequired();
        builder.Property(x => x.RatingCount).IsRequired();
        builder.Property(x => x.RatingTotal).IsRequired();
        builder.Property(x => x.ViewCount).IsRequired();
        builder.Property(x => x.CommentCount).IsRequired();
        builder.Property(x => x.ShareCount).IsRequired();
        builder.Property(x => x.ContributionSourceVersion).IsRequired();
        builder.Property(x => x.ContributionHighestSeenVersion).IsRequired();
        builder.Property(x => x.ContributionHasGap).IsRequired();
        builder.Property(x => x.HasExternalBaseline).IsRequired();
        builder.Property(x => x.IsErased).HasDefaultValue(false).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken().IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.Property(x => x.LastReconciledAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.HasOne<ResourceState>()
            .WithOne()
            .HasForeignKey<EngagementSummary>(x => new { x.ResourceType, x.ResourceId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LastReconciledAtUtc, x.ResourceType, x.ResourceId })
            .HasDatabaseName("IX_EngagementSummary_Reconcile");
    }
}

internal sealed class InteractionUserErasureConfiguration
    : IEntityTypeConfiguration<InteractionUserErasure>
{
    public void Configure(EntityTypeBuilder<InteractionUserErasure> builder)
    {
        builder.ToTable("InteractionUserErasures", table =>
        {
            table.HasCheckConstraint("CK_InteractionUserErasure_User", "`UserId` > 0");
            table.HasCheckConstraint("CK_InteractionUserErasure_Version", "`ErasureVersion` > 0 AND `FencingToken` >= 0");
            table.HasCheckConstraint("CK_InteractionUserErasure_Stage", "`Stage` IN (1,2,3)");
            table.HasCheckConstraint(
                "CK_InteractionUserErasure_Lease",
                "(`LeaseOwner` IS NULL AND `LeaseUntilUtc` IS NULL) OR (`LeaseOwner` IS NOT NULL AND `LeaseUntilUtc` IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_InteractionUserErasure_Completion",
                "(`Stage` = 3 AND `CompletedAtUtc` IS NOT NULL) OR (`Stage` <> 3 AND `CompletedAtUtc` IS NULL)");
        });
        builder.HasKey(item => item.UserId);
        builder.Property(item => item.UserId).ValueGeneratedNever();
        builder.Property(item => item.RequestId).HasColumnType("char(36)").IsRequired();
        builder.Property(item => item.ErasureVersion).IsRequired();
        builder.Property(item => item.RequestedAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.Property(item => item.RawViewsExpireAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.Property(item => item.Stage).HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(item => item.LeaseOwner).HasMaxLength(64).IsUnicode(false);
        builder.Property(item => item.LeaseUntilUtc).HasColumnType("datetime(6)");
        builder.Property(item => item.FencingToken).IsRequired();
        builder.Property(item => item.LastProcessedAtUtc).HasColumnType("datetime(6)");
        builder.Property(item => item.CompletedAtUtc).HasColumnType("datetime(6)");
        builder.HasIndex(item => new
        {
            item.Stage,
            item.RawViewsExpireAtUtc,
            item.LastProcessedAtUtc,
            item.LeaseUntilUtc,
            item.RequestedAtUtc,
            item.UserId,
        })
            .HasDatabaseName("IX_InteractionUserErasure_Work");
        builder.HasIndex(item => item.RequestId).IsUnique()
            .HasDatabaseName("UX_InteractionUserErasure_Request");
    }
}
