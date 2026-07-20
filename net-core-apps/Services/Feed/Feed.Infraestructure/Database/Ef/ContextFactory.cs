using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Inspacy.Data.MariaDb;

namespace Feed.Infra.Persistence
{
    /// <summary>Creates a Feed context exclusively for EF Core design-time tooling.</summary>
    /// <remarks>
    /// Design-time commands run outside the API host and its secret-aware configuration
    /// pipeline. The shared design-time helper owns the documented environment-read
    /// exception; runtime database configuration continues to flow through IConfiguration.
    /// </remarks>
    public class FeedContextactory : IDesignTimeDbContextFactory<FeedContext>
    {
        public FeedContext CreateDbContext(string[] args)
        {
            var options = MariaDbDesignTimeOptions.Create<FeedContext>();
            return new FeedContext(options);
        }
    }
}
