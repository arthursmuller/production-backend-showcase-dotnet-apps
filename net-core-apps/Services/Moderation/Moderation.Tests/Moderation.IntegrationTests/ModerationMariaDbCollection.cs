namespace Moderation.UnitTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ModerationMariaDbCollection
{
    public const string Name = "Moderation isolated MariaDB";
}
