using System;

namespace Comment.Application.Options;

public sealed class CommentMediaIngestionOptions
{
    public const string SectionName = "CommentMediaIngestion";

    public int ReconciliationBatchSize { get; set; } = 20;
    public int MaximumAttempts { get; set; } = 10;
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan RequestRecoveryDelay { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan FirstRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaximumRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan PendingAbandonment { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan PromotedOrphanRetention { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan TerminalRetention { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan IdleDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan ActiveDelay { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan FailureDelay { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    public bool IsValid() =>
        ReconciliationBatchSize is >= 1 and <= 100
        && MaximumAttempts is >= 1 and <= 100
        && InRange(LeaseDuration, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(30))
        && InRange(RequestRecoveryDelay, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(30))
        && InRange(FirstRetryDelay, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(5))
        && InRange(MaximumRetryDelay, FirstRetryDelay, TimeSpan.FromHours(1))
        && InRange(PendingAbandonment, RequestRecoveryDelay, TimeSpan.FromDays(1))
        && InRange(PromotedOrphanRetention, TimeSpan.FromMinutes(5), TimeSpan.FromDays(7))
        && InRange(TerminalRetention, TimeSpan.FromDays(1), TimeSpan.FromDays(365))
        && InRange(IdleDelay, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(5))
        && InRange(ActiveDelay, TimeSpan.FromMilliseconds(10), TimeSpan.FromMinutes(1))
        && InRange(FailureDelay, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(5))
        && InRange(CleanupInterval, TimeSpan.FromMinutes(1), TimeSpan.FromDays(1));

    public TimeSpan RetryDelay(int attempts)
    {
        var exponent = Math.Clamp(attempts, 0, 20);
        var ticks = FirstRetryDelay.Ticks * Math.Pow(2, exponent);
        return TimeSpan.FromTicks((long)Math.Min(ticks, MaximumRetryDelay.Ticks));
    }

    private static bool InRange(TimeSpan value, TimeSpan minimum, TimeSpan maximum) =>
        value >= minimum && value <= maximum;
}
