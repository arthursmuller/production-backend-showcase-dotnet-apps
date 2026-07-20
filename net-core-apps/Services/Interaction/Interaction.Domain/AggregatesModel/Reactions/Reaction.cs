using Interaction.Domain.Exceptions;

namespace Interaction.Domain.AggregatesModel;

public enum ReactionKind : byte
{
    Like = 1,
    Dislike = 2,
}

public sealed class Reaction
{
    private Reaction() { }

    public Reaction(ResourceRef resource, long actorId, ReactionKind kind, DateTimeOffset now)
    {
        if (actorId <= 0)
            throw new InteractionValidationException("Actor id must be positive.");
        if (!Enum.IsDefined(kind))
            throw new InteractionValidationException("Reaction kind is invalid.");
        ResourceType = resource.Type;
        ResourceId = resource.Id;
        ActorId = actorId;
        Kind = kind;
        Version = 1;
        UpdatedAtUtc = now;
    }

    public ResourceType ResourceType { get; private set; }
    public string ResourceId { get; private set; } = string.Empty;
    public long ActorId { get; private set; }
    public ReactionKind Kind { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool SwitchTo(ReactionKind kind, DateTimeOffset now)
    {
        if (!Enum.IsDefined(kind))
            throw new InteractionValidationException("Reaction kind is invalid.");
        if (Kind == kind)
            return false;
        Kind = kind;
        Version++;
        UpdatedAtUtc = now;
        return true;
    }
}
