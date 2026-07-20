namespace Moderation.Domain.Exceptions;

public sealed class ModerationValidationException(string message) : ArgumentException(message);

public sealed class ModerationConflictException(string message) : InvalidOperationException(message);

public sealed class ModerationConcurrencyException(long expected, long actual)
    : InvalidOperationException($"Expected case version {expected}, but the current version is {actual}.")
{
    public long ExpectedVersion { get; } = expected;
    public long ActualVersion { get; } = actual;
}
