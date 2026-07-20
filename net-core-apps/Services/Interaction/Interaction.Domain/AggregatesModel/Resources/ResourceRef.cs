using Interaction.Domain.Exceptions;

namespace Interaction.Domain.AggregatesModel;

public readonly record struct ResourceRef
{
    public const int MaximumIdLength = 128;

    public ResourceRef(ResourceType type, string id)
    {
        if (!Enum.IsDefined(type))
            throw new InteractionValidationException("Resource type is not supported.");
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var normalized = id.Trim();
        if (normalized.Length > MaximumIdLength)
            throw new InteractionValidationException($"Resource id cannot exceed {MaximumIdLength} characters.");
        if (normalized.Any(char.IsControl))
            throw new InteractionValidationException("Resource id cannot contain control characters.");

        Type = type;
        Id = normalized;
    }

    public ResourceType Type { get; }
    public string Id { get; }

    public static ResourceRef Parse(string type, string id)
    {
        if (!Enum.TryParse<ResourceType>(type, true, out var parsed) || !Enum.IsDefined(parsed))
            throw new InteractionValidationException("Resource type must be 'comment' or 'post'.");
        return new ResourceRef(parsed, id);
    }

    public override string ToString() => $"{Type.ToString().ToLowerInvariant()}:{Id}";
}
