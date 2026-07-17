using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace Comment.Infra.Persistence
{
    public class CommentContextactory : IDesignTimeDbContextFactory<CommentContext>
    {
        private static string _connectionString;

        public CommentContext CreateDbContext()
            => new CommentContext(null, null);

        public CommentContext CreateDbContext(string[] args)
        {
            if (string.IsNullOrEmpty(_connectionString))
            {
                LoadConnectionString();
            }

            var builder = new DbContextOptionsBuilder<CommentContext>();

            var version = new MariaDbServerVersion(new Version(10, 6, 11));
            builder.UseMySql(_connectionString, version)
                    .EnableSensitiveDataLogging();

            return new CommentContext(builder.Options, null);
            // mediator is null for design-time / migration scenarios — no events are dispatched
        }

        private static void LoadConnectionString()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath($"{Path.Combine(Directory.GetCurrentDirectory())}")
                .AddJsonFile("appsettings.json", optional: false);

            var configuration = builder.Build();
            _connectionString = configuration.GetConnectionString("SQL_CONNECTION_STRING");
        }
    }
}
