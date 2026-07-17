using DomainEventing;
using System.Collections.Generic;

namespace StarterEntities
{
    /// <summary>
    /// Extends <see cref="BaseEntity"/> with in-process domain-event buffering.
    /// Aggregate roots that need to raise domain events should inherit from this
    /// class instead of <see cref="BaseEntity"/> directly; they no longer need to
    /// redeclare the <c>_events</c> list, <c>DomainEvents</c> property, or
    /// <c>ClearEvents</c> method.
    /// <para>
    /// Events are dispatched by <see cref="DomainEventing.MediatorDbContextExtensions.DispatchDomainEventsAsync"/>
    /// (called inside <c>SaveEntitiesAsync</c>) before the underlying
    /// <c>SaveChangesAsync</c> so that handlers share the same EF Core
    /// transaction / DbContext scope.
    /// </para>
    /// </summary>
    public abstract class BaseEventingEntity : BaseEntity, IHasDomainEvents
    {
        protected BaseEventingEntity() { }
        protected BaseEventingEntity(int id) : base(id) { }

        private readonly List<IDomainEvent> _events = new();
        public IReadOnlyList<IDomainEvent> DomainEvents => _events.AsReadOnly();
        public void ClearEvents() => _events.Clear();

        /// <summary>Queues a domain event to be dispatched after (or before) persistence.</summary>
        protected void RaiseEvent(IDomainEvent @event) => _events.Add(@event);
    }
}
