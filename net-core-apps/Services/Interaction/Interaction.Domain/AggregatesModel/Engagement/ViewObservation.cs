namespace Interaction.Domain.AggregatesModel;

public sealed class ViewObservation
{
    private ViewObservation() { }

    public ViewObservation(
        ResourceRef resource,
        string observerHash,
        DateTimeOffset bucketStartUtc,
        DateTimeOffset receivedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        ResourceType = resource.Type;
        ResourceId = resource.Id;
        ObserverHash = observerHash;
        BucketStartUtc = bucketStartUtc;
        ReceivedAtUtc = receivedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public long Id { get; private set; }
    public ResourceType ResourceType { get; private set; }
    public string ResourceId { get; private set; } = string.Empty;
    public string ObserverHash { get; private set; } = string.Empty;
    public DateTimeOffset BucketStartUtc { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public void MarkProcessed(DateTimeOffset now) => ProcessedAtUtc ??= now;
}
