using Inspacy.Data.MariaDb;
using Microsoft.EntityFrameworkCore.Design;

namespace Moderation.Infra.Database.Ef;

/// <summary>Design-time-only factory; runtime settings flow through the secret-aware host configuration.</summary>
public sealed class ModerationDbContextFactory : IDesignTimeDbContextFactory<ModerationDbContext>
{
    public ModerationDbContext CreateDbContext(string[] args)
    {
        var options = MariaDbDesignTimeOptions.Create<ModerationDbContext>();
        return new ModerationDbContext(options);
    }
}
