using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace Following.Infra.Persistence
{
    public class FollowingContextactory : IDesignTimeDbContextFactory<FollowingContext>
    {
        private static string _connectionString;

        public FollowingContext CreateDbContext()
            => new FollowingContext(null, null);

        public FollowingContext CreateDbContext(string[] args)
        {
            if (string.IsNullOrEmpty(_connectionString))
            {
                LoadConnectionString();
            }

            var builder = new DbContextOptionsBuilder<FollowingContext>();

            var version = new MariaDbServerVersion(new Version(10, 6, 11));
            builder.UseMySql(_connectionString, version)
                    .EnableSensitiveDataLogging();

            return new FollowingContext(builder.Options, null);
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
