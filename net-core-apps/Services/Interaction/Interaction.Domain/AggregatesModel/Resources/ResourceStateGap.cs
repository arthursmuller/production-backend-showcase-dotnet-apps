namespace Interaction.Domain.AggregatesModel;

public sealed class ResourceStateGap
{
    private ResourceStateGap() { }

    public ResourceStateGap(
        ResourceRef resource,
        long sourceVersion,
        ResourceLifecycle lifecycle,
        ResourceVisibility visibility,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset receivedAtUtc)
    {
        ResourceType = resource.Type;
        ResourceId = resource.Id;
        SourceVersion = sourceVersion;
        Lifecycle = lifecycle;
        Visibility = visibility;
        OccurredAtUtc = occurredAtUtc;
        ReceivedAtUtc = receivedAtUtc;
    }

    public ResourceType ResourceType { get; private set; }
    public string ResourceId { get; private set; } = string.Empty;
    public long SourceVersion { get; private set; }
    public ResourceLifecycle Lifecycle { get; private set; }
    public ResourceVisibility Visibility { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
}
