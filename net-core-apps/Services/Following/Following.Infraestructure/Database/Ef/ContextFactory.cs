using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Inspacy.Data.MariaDb;

namespace Following.Infra.Persistence
{
    /// <summary>Creates a Following context exclusively for EF Core design-time tooling.</summary>
    /// <remarks>
    /// Design-time commands run outside the API host and its secret-aware configuration
    /// pipeline. The shared design-time helper owns the documented environment-read
    /// exception; runtime database configuration continues to flow through IConfiguration.
    /// </remarks>
    public class FollowingContextFactory : IDesignTimeDbContextFactory<FollowingContext>
    {
        public FollowingContext CreateDbContext(string[] args)
        {
            var options = MariaDbDesignTimeOptions.Create<FollowingContext>();
            return new FollowingContext(options);
        }
    }
}
