using Following.Domain.AggregatesModel;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace Following.Infra.Persistence
{
    public class FollowingContext : DbContext
    {
        public DbSet<FriendRequest> FriendRequests { get; set; }
        public DbSet<Friendship> Friendships { get; set; }
        public DbSet<Follow> Follows { get; set; }
        public DbSet<CategoryFollow> CategoryFollows { get; set; }
        public DbSet<UserRelationAnalytics> UserRelationsAnalytics { get; set; }
        public DbSet<FriendshipSubscriptionFeature> FriendsSubscriptionsFeature { get; set; }
        public DbSet<RelationshipEventStream> RelationshipEventStreams { get; set; }
        public DbSet<UserBlock> UserBlocks { get; set; }
        public DbSet<FollowingUserErasure> UserErasures { get; set; }

        public FollowingContext(DbContextOptions<FollowingContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(FollowingContext).Assembly);
            modelBuilder.AddInboxStateEntity();
            modelBuilder.AddOutboxMessageEntity();
            modelBuilder.AddOutboxStateEntity();
            ConfigureErasureFilters(modelBuilder);
            base.OnModelCreating(modelBuilder);
        }

        private void ConfigureErasureFilters(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Follow>().HasQueryFilter(item =>
                !UserErasures.Any(erasure => erasure.UserId == item.FollowerUserId)
                && !UserErasures.Any(erasure => erasure.UserId == item.FollowedUserId));
            modelBuilder.Entity<Friendship>().HasQueryFilter(item =>
                !UserErasures.Any(erasure => erasure.UserId == item.UserId)
                && !UserErasures.Any(erasure => erasure.UserId == item.FriendUserId));
            modelBuilder.Entity<FriendRequest>().HasQueryFilter(item =>
                !UserErasures.Any(erasure => erasure.UserId == item.SenderUserId)
                && !UserErasures.Any(erasure => erasure.UserId == item.RecipientUserId));
            modelBuilder.Entity<UserBlock>().HasQueryFilter(item =>
                !UserErasures.Any(erasure => erasure.UserId == item.BlockerUserId)
                && !UserErasures.Any(erasure => erasure.UserId == item.BlockedUserId));
            modelBuilder.Entity<FriendshipSubscriptionFeature>().HasQueryFilter(item =>
                !UserErasures.Any(erasure => erasure.UserId == item.OwnerId)
                && !UserErasures.Any(erasure => erasure.UserId == item.RequesterId));
            modelBuilder.Entity<CategoryFollow>().HasQueryFilter(item =>
                !UserErasures.Any(erasure => erasure.UserId == item.UserId));
            modelBuilder.Entity<UserRelationAnalytics>().HasQueryFilter(item =>
                !UserErasures.Any(erasure => erasure.UserId == item.Id));
            modelBuilder.Entity<RelationshipEventStream>().HasQueryFilter(item =>
                !UserErasures.Any(erasure => erasure.UserId == item.OwnerUserId)
                && !UserErasures.Any(erasure => erasure.UserId == item.ViewerUserId));
        }
    }
}
