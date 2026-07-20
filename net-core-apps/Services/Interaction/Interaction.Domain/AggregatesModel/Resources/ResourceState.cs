using Interaction.Domain.Exceptions;

namespace Interaction.Domain.AggregatesModel;

public sealed class ResourceState
{
    private ResourceState() { }

    public ResourceState(ResourceRef resource, DateTimeOffset now)
    {
        ResourceType = resource.Type;
        ResourceId = resource.Id;
        Lifecycle = ResourceLifecycle.Unknown;
        Visibility = ResourceVisibility.Private;
        UpdatedAtUtc = now;
    }

    public ResourceType ResourceType { get; private set; }
    public string ResourceId { get; private set; } = string.Empty;
    public ResourceLifecycle Lifecycle { get; private set; }
    public ResourceVisibility Visibility { get; private set; }
    public long AppliedVersion { get; private set; }
    public long HighestSeenVersion { get; private set; }
    public bool HasGap { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public ResourceRef Resource => new(ResourceType, ResourceId);
    public bool IsPubliclyInteractable =>
        !HasGap && Lifecycle == ResourceLifecycle.Active && Visibility == ResourceVisibility.Public;
    public bool IsActive => !HasGap && Lifecycle == ResourceLifecycle.Active;
    public bool IsDeleted => !HasGap && Lifecycle == ResourceLifecycle.Deleted;

    public bool ObserveGap(long sourceVersion, DateTimeOffset now)
    {
        if (sourceVersion <= AppliedVersion)
            return false;
        HighestSeenVersion = Math.Max(HighestSeenVersion, sourceVersion);
        HasGap = true;
        UpdatedAtUtc = now;
        ConcurrencyVersion++;
        return true;
    }

    public bool ApplyNext(
        ResourceLifecycle lifecycle,
        ResourceVisibility visibility,
        long sourceVersion,
        DateTimeOffset now)
    {
        if (sourceVersion <= AppliedVersion)
            return false;
        if (sourceVersion != AppliedVersion + 1)
            throw new InteractionConflictException("A resource projection version gap was detected.");

        Lifecycle = lifecycle;
        Visibility = visibility;
        AppliedVersion = sourceVersion;
        HighestSeenVersion = Math.Max(HighestSeenVersion, sourceVersion);
        HasGap = AppliedVersion < HighestSeenVersion;
        UpdatedAtUtc = now;
        ConcurrencyVersion++;
        return true;
    }

    public bool ApplyBaseline(
        ResourceLifecycle lifecycle,
        ResourceVisibility visibility,
        long sourceVersion,
        DateTimeOffset now)
    {
        if (AppliedVersion != 0 || sourceVersion <= 0)
            return false;
        Lifecycle = lifecycle;
        Visibility = visibility;
        AppliedVersion = sourceVersion;
        HighestSeenVersion = sourceVersion;
        HasGap = false;
        UpdatedAtUtc = now;
        ConcurrencyVersion++;
        return true;
    }

    public void RefreshGapState() => HasGap = AppliedVersion < HighestSeenVersion;
}
