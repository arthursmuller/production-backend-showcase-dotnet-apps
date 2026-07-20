using DomainExceptions;

namespace Interaction.Domain.Exceptions;

public sealed class InteractionValidationException(string message) : DomainException(message, 400);
public sealed class InteractionUnauthorizedException(string message) : DomainException(message, 401);
public sealed class InteractionNotFoundException(string message) : DomainException(message, 404);
public sealed class InteractionConflictException : DomainException
{
    public InteractionConflictException(string message) : base(message, 409) { }
    public InteractionConflictException(string message, Exception innerException) : base(message, innerException, 409) { }
}
