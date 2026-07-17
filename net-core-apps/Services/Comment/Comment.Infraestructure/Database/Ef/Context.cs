using System;
using System.Linq;
using System.Data;
using System.Threading;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Comment.Domain.AggregatesModel;
using DomainEventing;
using Mediator;

namespace Comment.Infra.Persistence
{
    public class CommentContext : DbContext
    {
        public DbSet<CommentPost> Comments { get; set; }
        public DbSet<CommentSocialAnalytics> CommentRatingsAnalytics { get; set; }
        public DbSet<CommentSocialRating> CommentsRating { get; set; }
        public DbSet<CommentLike> CommentLikes { get; set; }
        public DbSet<CommentDislike> CommentDislikes { get; set; }
        public DbSet<CommentRatingLevel> CommentRatingLevels { get; set; }
        public DbSet<CommentReport> CommentReports { get; set; }
        public DbSet<CommentPostComment> CommentComments { get; set; }
        public DbSet<CommentShare> CommentShares { get; set; }
        public DbSet<CommentBookmark> CommentBookmarks { get; set; }

        private readonly IMediator _mediator;

        public CommentContext(DbContextOptions<CommentContext> options, IMediator mediator) : base(options)
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