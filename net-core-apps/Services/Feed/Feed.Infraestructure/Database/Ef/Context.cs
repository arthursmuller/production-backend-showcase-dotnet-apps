using Feed.Domain.AggregatesModel;
using Feed.Domain.AggregatesModel.Moderation;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Feed.Infra.Persistence
{
    public class FeedContext : DbContext
    {
        public DbSet<FeedActivity> FeedActivities { get; set; }
        public DbSet<FeedEntry> FeedEntries { get; set; }
        public DbSet<FeedAudienceEdge> FeedAudienceEdges { get; set; }
        public DbSet<FeedAudienceBootstrapState> FeedAudienceBootstrapStates { get; set; }
        public DbSet<FeedRelationshipCheckpoint> FeedRelationshipCheckpoints { get; set; }
        public DbSet<FeedRelationshipEventQuarantine> FeedRelationshipEventQuarantines { get; set; }
        public DbSet<FeedUserState> FeedUserStates { get; set; }
        public DbSet<FanoutWorkItem> FanoutWorkItems { get; set; }
        public DbSet<FeedPublisherState> FeedPublisherStates { get; set; }
        public DbSet<FeedRevocationWorkItem> FeedRevocationWorkItems { get; set; }
        public DbSet<FeedMaintenanceCheckpoint> FeedMaintenanceCheckpoints { get; set; }
        public DbSet<FeedActivityEventQuarantine> FeedActivityEventQuarantines { get; set; }
        public DbSet<FeedEngagementSummary> FeedEngagementSummaries { get; set; }
        public DbSet<FeedEngagementSummaryEventQuarantine> FeedEngagementSummaryEventQuarantines { get; set; }
        public DbSet<FeedPullCandidateRow> FeedPullCandidateRows { get; set; }
        public DbSet<FeedUserErasure> FeedUserErasures { get; set; }
        public DbSet<FeedUserMutationGuard> FeedUserMutationGuards { get; set; }
        public DbSet<FeedModerationDecisionState> FeedModerationDecisionStates { get; set; }
        public DbSet<FeedModerationDecisionGap> FeedModerationDecisionGaps { get; set; }

        public FeedContext(DbContextOptions<FeedContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(FeedContext).Assembly);
            modelBuilder.AddInboxStateEntity();
            modelBuilder.AddOutboxMessageEntity();
            modelBuilder.AddOutboxStateEntity();
            base.OnModelCreating(modelBuilder);
        }
    }
}
