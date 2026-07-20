using StarterEntities;
using System;
using System.Collections.Generic;

namespace DomainEventing
{
    /// <summary>
    /// Base type for entities that collect domain events as part of the current
    /// unit of work. Events are exposed read-only and can only be raised by the
    /// entity itself.
    /// </summary>
    public abstract class DomainBaseEntity : BaseEntity, IHasDomainEvents
    {
        private readonly List<IDomainEvent> _events = new();

        protected DomainBaseEntity() { }

        protected DomainBaseEntity(int id) : base(id) { }

        protected DomainBaseEntity(DateTime createdDate) : base(createdDate) { }

        public IReadOnlyList<IDomainEvent> DomainEvents => _events.AsReadOnly();

        public void ClearEvents() => _events.Clear();

        protected void RaiseEvent(IDomainEvent domainEvent)
        {
            if (domainEvent is null)
                throw new ArgumentNullException(nameof(domainEvent));

            _events.Add(domainEvent);
        }
    }
}
