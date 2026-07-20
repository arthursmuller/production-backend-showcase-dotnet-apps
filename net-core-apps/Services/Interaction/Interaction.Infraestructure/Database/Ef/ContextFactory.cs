using Inspacy.Data.MariaDb;
using Microsoft.EntityFrameworkCore.Design;

namespace Interaction.Infraestructure.Database.Ef;

public sealed class InteractionDbContextFactory : IDesignTimeDbContextFactory<InteractionDbContext>
{
    public InteractionDbContext CreateDbContext(string[] args)
    {
        var options = MariaDbDesignTimeOptions.Create<InteractionDbContext>();
        return new InteractionDbContext(options);
    }
}
