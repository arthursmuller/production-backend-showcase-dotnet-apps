namespace Moderation.Domain.AggregatesModel;

/// <summary>
/// Singleton durable lease and progress aggregate for destructive Moderation maintenance.
/// The database store changes it atomically so multiple JobWorker replicas cannot run the
/// same retention cycle concurrently.
/// </summary>
public sealed class ModerationRetentionCheckpoint
{
    public const int SingletonId = 1;

    private ModerationRetentionCheckpoint() { }

    public int Id { get; private set; } = SingletonId;
    public string? LeaseOwner { get; private set; }
    public DateTimeOffset? LeaseUntilUtc { get; private set; }
    public long FencingToken { get; private set; }
    public DateTimeOffset? LastStartedAtUtc { get; private set; }
    public DateTimeOffset? LastCompletedAtUtc { get; private set; }
    public long TotalCasesRedacted { get; private set; }
    public long TotalCasesPurged { get; private set; }
}
