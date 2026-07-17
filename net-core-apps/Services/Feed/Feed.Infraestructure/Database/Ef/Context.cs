using System;
using System.Linq;
using System.Data;
using System.Threading;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Feed.Domain.AggregatesModel;
using DomainEventing;
using Mediator;

namespace Feed.Infra.Persistence
{
    public class FeedContext : DbContext
    {
        public DbSet<UserConnectionsFeed> UserConnectionsFeed { get; set; }
        public DbSet<Post> Posts { get; set; }

        private readonly IMediator _mediator;

        public FeedContext(DbContextOptions<FeedContext> options, IMediator mediator) : base(options)
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