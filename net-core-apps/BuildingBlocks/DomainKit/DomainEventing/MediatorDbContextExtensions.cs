using System.Linq;
using System.Threading.Tasks;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DomainEventing
{
    /// <summary>
    /// Extension method that mirrors the eShop pattern:
    /// dispatch all pending domain events from tracked aggregates
    /// <em>before</em> the underlying <c>SaveChangesAsync</c> call so that
    /// side-effect operations share the same EF Core transaction/DbContext scope.
    /// </summary>
    public static class MediatorDbContextExtensions
    {
        /// <summary>
        /// Publishes every domain event that is queued on any <see cref="IHasDomainEvents"/>
        /// entity currently tracked by <paramref name="context"/>, then clears
        /// the event collections.
        ///
        /// Call this method right before <c>base.SaveChangesAsync()</c> inside a
        /// custom <c>SaveEntitiesAsync</c> override on your DbContext.
        /// </summary>
        public static async Task DispatchDomainEventsAsync(this IMediator mediator, DbContext context)
        {
            var domainEntities = context.ChangeTracker
                .Entries<IHasDomainEvents>()
                .Where(entry => entry.Entity.DomainEvents.Count > 0)
                .ToList();

            var allEvents = domainEntities
                .SelectMany(entry => entry.Entity.DomainEvents)
                .ToList();

            // Clear before dispatching so that handlers that also trigger
            // SaveChangesAsync don't re-dispatch the same events.
            domainEntities.ForEach(entry => entry.Entity.ClearEvents());

            foreach (var @event in allEvents)
                await mediator.Publish(@event);
        }
    }
}
