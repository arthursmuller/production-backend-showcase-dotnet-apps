using Mediator;

namespace DomainEventing
{
    /// <summary>
    /// Marker interface for all in-process domain events.
    /// Implements <see cref="INotification"/> so every domain event is
    /// automatically dispatchable through the Mediator library without any
    /// extra plumbing.
    /// </summary>
    public interface IDomainEvent : INotification { }
}
