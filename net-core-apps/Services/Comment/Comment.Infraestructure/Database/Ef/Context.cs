using Comment.Domain.AggregatesModel;
using Comment.Domain.AggregatesModel.Interactions;
using Comment.Domain.AggregatesModel.Media;
using Comment.Domain.AggregatesModel.Visibility;
using Comment.Domain.AggregatesModel.Privacy;
using Comment.Domain.AggregatesModel.Moderation;
using DomainEventing;
using MassTransit;
using Mediator;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Persistence
{
    public class CommentContext : DbContext
    {
        public DbSet<CommentPost> Comments { get; set; }
        public DbSet<CommentInteractionSummaryProjection> CommentInteractionSummaryProjections { get; set; }
        public DbSet<CommentInteractionActorProjection> CommentInteractionActorProjections { get; set; }
        public DbSet<CommentInteractionSummaryGap> CommentInteractionSummaryGaps { get; set; }
        public DbSet<CommentMediaIngestion> CommentMediaIngestions { get; set; }
        public DbSet<CommentRelationshipCheckpoint> CommentRelationshipCheckpoints { get; set; }
        public DbSet<CommentRelationshipEdge> CommentRelationshipEdges { get; set; }
        public DbSet<CommentRelationshipEventQuarantine> CommentRelationshipEventQuarantines { get; set; }
        public DbSet<CommentAudienceProjectionState> CommentAudienceProjectionStates { get; set; }
        public DbSet<CommentUserErasure> CommentUserErasures { get; set; }
        public DbSet<CommentErasureMediaObject> CommentErasureMediaObjects { get; set; }
        public DbSet<CommentUserMutationGuard> CommentUserMutationGuards { get; set; }
        public DbSet<CommentModerationDecisionProjection> CommentModerationDecisionProjections { get; set; }
        public DbSet<CommentModerationDecisionGap> CommentModerationDecisionGaps { get; set; }

        private readonly IMediator? _mediator;

        public CommentContext(
            DbContextOptions<CommentContext> options,
            IMediator? mediator = null)
            : base(options) => _mediator = mediator;

        /// <summary>
        /// Dispatches pending domain events before persistence. Comment handlers
        /// only stage outbox rows, so aggregate state and delivery intent are
        /// committed atomically in the same EF transaction.
        /// </summary>
        public async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken)
        {
            var domainEntities = ChangeTracker
                .Entries<IHasDomainEvents>()
                .Where(entry => entry.Entity.DomainEvents.Count > 0)
                .ToList();
            var domainEvents = domainEntities
                .SelectMany(entry => entry.Entity.DomainEvents)
                .ToList();

            if (domainEvents.Count > 0)
            {
                var mediator = _mediator ?? throw new InvalidOperationException(
                    "A mediator is required to persist Comment domain events.");

                domainEntities.ForEach(entry => entry.Entity.ClearEvents());
                foreach (var domainEvent in domainEvents)
                    await mediator.Publish(domainEvent, cancellationToken);
            }

            await SaveChangesAsync(cancellationToken);
            return true;
        }

        public override int SaveChanges() => SaveChanges(acceptAllChangesOnSuccess: true);

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            PrepareConcurrencyTrackedEntities();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default) =>
            SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            PrepareConcurrencyTrackedEntities();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(CommentContext).Assembly);
            modelBuilder.AddInboxStateEntity();
            modelBuilder.AddOutboxMessageEntity();
            modelBuilder.AddOutboxStateEntity();
            base.OnModelCreating(modelBuilder);
        }

        private void PrepareConcurrencyTrackedEntities()
        {
            foreach (var entry in ChangeTracker.Entries<ICommentConcurrencyTracked>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            {
                var version = entry.Property<long>(nameof(ICommentConcurrencyTracked.Version));
                if (entry.State == EntityState.Added)
                {
                    if ((long)version.CurrentValue <= 0)
                        version.CurrentValue = 1L;
                    continue;
                }

                version.CurrentValue = checked((long)version.OriginalValue + 1L);
                if (entry.Entity is StarterEntities.BaseEntity entity)
                    entity.UpdateDate = DateTime.UtcNow;
            }
        }
    }
}
