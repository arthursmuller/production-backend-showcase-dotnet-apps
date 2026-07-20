namespace Interaction.Domain.AggregatesModel;

/// <summary>
/// Immutable, database-appended recovery revision for one complete resource authority row.
/// It contains no actor-level interaction data.
/// </summary>
public sealed class InteractionAuthoritativeStateRevision
{
    private InteractionAuthoritativeStateRevision() { }

    public long RevisionCursor { get; private set; }
    public ResourceType ResourceType { get; private set; }
    public string ResourceId { get; private set; } = string.Empty;
    public ResourceLifecycle Lifecycle { get; private set; }
    public ResourceVisibility Visibility { get; private set; }
    public long AppliedResourceVersion { get; private set; }
    public long HighestSeenResourceVersion { get; private set; }
    public bool ResourceHasGap { get; private set; }
    public long ResourceConcurrencyVersion { get; private set; }
    public DateTimeOffset ResourceUpdatedAtUtc { get; private set; }
    public long? LikeCount { get; private set; }
    public long? DislikeCount { get; private set; }
    public long? RatingCount { get; private set; }
    public long? RatingTotal { get; private set; }
    public long? ViewCount { get; private set; }
    public long? CommentCount { get; private set; }
    public long? ShareCount { get; private set; }
    public long? ContributionSourceVersion { get; private set; }
    public long? ContributionHighestSeenVersion { get; private set; }
    public bool? ContributionHasGap { get; private set; }
    public bool? HasExternalBaseline { get; private set; }
    public bool? IsErased { get; private set; }
    public long? SummaryVersion { get; private set; }
    public DateTimeOffset? SummaryUpdatedAtUtc { get; private set; }
    public byte PartitionKey { get; private set; }
    public string RowSha256 { get; private set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; private set; }
}
