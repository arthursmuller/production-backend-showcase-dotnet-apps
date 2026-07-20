using Interaction.Domain.AggregatesModel;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Interaction.Infraestructure.Database.Ef;

public sealed class InteractionDbContext(DbContextOptions<InteractionDbContext> options) : DbContext(options)
{
    public DbSet<InteractionAuthorityCoordinate> AuthorityCoordinates => Set<InteractionAuthorityCoordinate>();
    public DbSet<InteractionAuthoritativeStateRevision> AuthorityRevisions => Set<InteractionAuthoritativeStateRevision>();
    public DbSet<ResourceState> ResourceStates => Set<ResourceState>();
    public DbSet<ResourceStateGap> ResourceStateGaps => Set<ResourceStateGap>();
    public DbSet<Reaction> Reactions => Set<Reaction>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<ViewObservation> ViewObservations => Set<ViewObservation>();
    public DbSet<EngagementSummary> EngagementSummaries => Set<EngagementSummary>();
    public DbSet<InteractionUserErasure> UserErasures => Set<InteractionUserErasure>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InteractionDbContext).Assembly);
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AddAuthorityCoordinatesForNewResources();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        AddAuthorityCoordinatesForNewResources();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void AddAuthorityCoordinatesForNewResources()
    {
        ChangeTracker.DetectChanges();
        var newResources = ChangeTracker.Entries<ResourceState>()
            .Where(item => item.State == EntityState.Added)
            .Select(item => item.Entity)
            .ToArray();
        foreach (var state in newResources)
        {
            if (AuthorityCoordinates.Local.Any(item =>
                    item.ResourceType == state.ResourceType
                    && item.ResourceId == state.ResourceId))
            {
                continue;
            }

            AuthorityCoordinates.Add(
                InteractionAuthorityCoordinate.Create(state.Resource, state.UpdatedAtUtc));
        }
    }
}
