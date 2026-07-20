using Comment.Domain.AggregatesModel.Interactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comment.Infra.Persistence.Configurations.Interactions;

internal sealed class CommentInteractionActorProjectionConfiguration
    : IEntityTypeConfiguration<CommentInteractionActorProjection>
{
    public void Configure(EntityTypeBuilder<CommentInteractionActorProjection> builder)
    {
        builder.ToTable("CommentInteractionActorProjections", table =>
        {
            table.HasCheckConstraint(
                "CK_CommentInteractionActorProjection_Versions",
                "`ReactionVersion` >= 0 AND `RatingVersion` >= 0");
            table.HasCheckConstraint(
                "CK_CommentInteractionActorProjection_Reaction",
                "`Reaction` IS NULL OR `Reaction` IN (1,2)");
            table.HasCheckConstraint(
                "CK_CommentInteractionActorProjection_Rating",
                "`Rating` IS NULL OR `Rating` BETWEEN 1 AND 6");
        });
        builder.HasKey(x => new { x.CommentId, x.ActorId });
        builder.Property(x => x.Reaction).HasColumnType("tinyint unsigned");
        builder.Property(x => x.Rating).HasColumnType("tinyint unsigned");
        builder.Property(x => x.ReactionVersion).IsConcurrencyToken().IsRequired();
        builder.Property(x => x.RatingVersion).IsConcurrencyToken().IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.HasIndex(x => new { x.ActorId, x.UpdatedAtUtc })
            .HasDatabaseName("IX_CommentInteractionActor_Actor_Updated");
    }
}
