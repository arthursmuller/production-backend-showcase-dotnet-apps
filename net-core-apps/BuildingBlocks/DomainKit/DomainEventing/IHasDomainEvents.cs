using System.Collections.Generic;

namespace DomainEventing
{
    /// <summary>
    /// Implemented by aggregate roots that collect domain events during a
    /// unit-of-work so they can be dispatched after (or before) persistence.
    /// </summary>
    public interface IHasDomainEvents
    {
        IReadOnlyList<IDomainEvent> DomainEvents { get; }
        void ClearEvents();
    }
}
