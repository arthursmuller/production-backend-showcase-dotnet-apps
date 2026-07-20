using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Inspacy.Data.MariaDb;

namespace Comment.Infra.Persistence
{
    /// <summary>Creates a Comment context exclusively for EF Core design-time tooling.</summary>
    /// <remarks>
    /// Design-time commands run outside the API host and its secret-aware configuration
    /// pipeline. The shared design-time helper owns the documented environment-read
    /// exception; runtime database configuration continues to flow through IConfiguration.
    /// </remarks>
    public class CommentContextactory : IDesignTimeDbContextFactory<CommentContext>
    {
        public CommentContext CreateDbContext(string[] args)
        {
            var options = MariaDbDesignTimeOptions.Create<CommentContext>();
            return new CommentContext(options);
            // mediator is null for design-time / migration scenarios — no events are dispatched
        }
    }
}
