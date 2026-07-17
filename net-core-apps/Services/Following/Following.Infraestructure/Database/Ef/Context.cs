using System;
using System.Linq;
using System.Data;
using System.Threading;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Following.Domain.AggregatesModel;
using DomainEventing;
using Mediator;

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

        private readonly IMediator _mediator;

        public FollowingContext(DbContextOptions<FollowingContext> options, IMediator mediator) : base(options)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Dispatches all pending domain events from tracked aggregates
        /// <em>before</em> persisting to the database so that side effects
        /// run inside the same EF Core transaction / DbContext scope.
        /// (eShop deferred domain-events pattern)
        /// </summary>
        public async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
        {
            await _mediator.DispatchDomainEventsAsync(this);
            await base.SaveChangesAsync(cancellationToken);
            return true;
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            applyConfigurationsFromAssembly(modelBuilder);

            base.OnModelCreating(modelBuilder);
        }
        private void applyConfigurationsFromAssembly(ModelBuilder modelBuilder)
        {
            var configurationTypes = Assembly.GetExecutingAssembly()
                    .GetTypes()
                    .Where(type => type.GetInterfaces()
                        .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)))
                    .ToList();

            foreach (var configurationType in configurationTypes)
            {
                dynamic configurationInstance = Activator.CreateInstance(configurationType);
                modelBuilder.ApplyConfiguration(configurationInstance);
            }
        }
    }
}