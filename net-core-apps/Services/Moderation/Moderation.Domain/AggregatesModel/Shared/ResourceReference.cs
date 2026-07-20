using System.Text.RegularExpressions;
using Moderation.Domain.Exceptions;

namespace Moderation.Domain.AggregatesModel;

public sealed record ResourceReference
{
    private static readonly Regex IdentifierPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9:_-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public ResourceKind Kind { get; }
    public string ResourceId { get; }

    public ResourceReference(ResourceKind kind, string resourceId)
    {
        if (!Enum.IsDefined(kind))
            throw new ModerationValidationException("The resource kind is not allow-listed.");
        if (string.IsNullOrWhiteSpace(resourceId) || !IdentifierPattern.IsMatch(resourceId))
        {
            throw new ModerationValidationException(
                "ResourceId must be an opaque 1-128 character identifier, not a URL.");
        }

        Kind = kind;
        ResourceId = resourceId;
    }
}
