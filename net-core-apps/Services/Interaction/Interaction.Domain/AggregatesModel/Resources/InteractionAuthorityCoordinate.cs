namespace Interaction.Domain.AggregatesModel;

/// <summary>
/// Immutable creation coordinate for the authoritative resource export. The generated cursor
/// fixes snapshot membership without holding a database transaction across HTTP requests.
/// </summary>
public sealed class InteractionAuthorityCoordinate
{
    private InteractionAuthorityCoordinate() { }

    private InteractionAuthorityCoordinate(ResourceRef resource, DateTimeOffset createdAtUtc)
    {
        ResourceType = resource.Type;
        ResourceId = resource.Id;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public long Cursor { get; private set; }
    public ResourceType ResourceType { get; private set; }
    public string ResourceId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static InteractionAuthorityCoordinate Create(
        ResourceRef resource,
        DateTimeOffset createdAtUtc) => new(resource, createdAtUtc);
}
