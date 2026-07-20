using Xunit;

namespace Interaction.UnitTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InteractionMariaDbCollection
{
    public const string Name = "Interaction isolated MariaDB";
}
