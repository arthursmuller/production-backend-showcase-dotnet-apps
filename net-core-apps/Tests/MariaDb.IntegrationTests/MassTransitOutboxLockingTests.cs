using System.Data;
using Feed.Infra.Persistence;
using Following.Infra.Persistence;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Xunit;

namespace MariaDb.IntegrationTests;

public sealed class MassTransitOutboxLockingTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public Task Feed_two_delivery_replicas_cannot_claim_the_same_outbox() =>
        AssertExclusiveDeliveryClaim(
            "feed",
            connectionString =>
            {
                var options = new DbContextOptionsBuilder<FeedContext>()
                    .UseMySql(
                        connectionString,
                        new MariaDbServerVersion(new Version(10, 6, 0)),
                        provider => provider.CommandTimeout(10))
                    .Options;
                return new FeedContext(options);
            });

    [Fact]
    [Trait("Category", "Integration")]
    public Task Following_two_delivery_replicas_cannot_claim_the_same_outbox() =>
        AssertExclusiveDeliveryClaim(
            "following",
            connectionString =>
            {
                var options = new DbContextOptionsBuilder<FollowingContext>()
                    .UseMySql(
                        connectionString,
                        new MariaDbServerVersion(new Version(10, 6, 0)),
                        provider => provider.CommandTimeout(10))
                    .Options;
                return new FollowingContext(options);
            });

    private static async Task AssertExclusiveDeliveryClaim<TContext>(
        string service,
        Func<string, TContext> createContext)
        where TContext : DbContext
    {
        var configuredConnection = Environment.GetEnvironmentVariable("TEST_MARIADB_CONNECTION");
        if (string.IsNullOrWhiteSpace(configuredConnection))
        {
            Assert.Skip(
                "Set TEST_MARIADB_CONNECTION to run the isolated MariaDB integration suite.");
        }

        var databaseName = $"mt_outbox_{service}_{Guid.NewGuid():N}";
        Assert.StartsWith($"mt_outbox_{service}_", databaseName, StringComparison.Ordinal);
        var databaseConnection = WithDatabase(configuredConnection!, databaseName);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var databaseCreated = false;

        try
        {
            await ExecuteAdministrativeCommand(
                configuredConnection!,
                $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;",
                timeout.Token);
            databaseCreated = true;

            var outboxId = Guid.NewGuid();
            await using (var setup = createContext(databaseConnection))
            {
                await setup.Database.MigrateAsync(timeout.Token);
                setup.Set<OutboxState>().Add(new OutboxState
                {
                    OutboxId = outboxId,
                    LockId = Guid.Empty,
                    Created = DateTime.UtcNow,
                });
                await setup.SaveChangesAsync(timeout.Token);
            }

            await using var firstReplica = createContext(databaseConnection);
            await using var secondReplica = createContext(databaseConnection);
            var lockProvider = new MySqlLockStatementProvider(enableSchemaCaching: false);
            var firstClaimSql = lockProvider.GetOutboxStatement(firstReplica);
            var secondClaimSql = lockProvider.GetOutboxStatement(secondReplica);
            Assert.Contains("FOR UPDATE SKIP LOCKED", firstClaimSql, StringComparison.Ordinal);

            await using var firstTransaction = await firstReplica.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                timeout.Token);
            var firstClaim = await firstReplica.Set<OutboxState>()
                .FromSqlRaw(firstClaimSql)
                .AsTracking()
                .SingleAsync(timeout.Token);
            var firstLockId = Guid.NewGuid();
            firstClaim.LockId = firstLockId;
            await firstReplica.SaveChangesAsync(timeout.Token);

            await using var secondTransaction = await secondReplica.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                timeout.Token);
            var competingClaims = await secondReplica.Set<OutboxState>()
                .FromSqlRaw(secondClaimSql)
                .AsTracking()
                .ToListAsync(timeout.Token);
            Assert.Empty(competingClaims);

            await firstTransaction.CommitAsync(timeout.Token);

            var secondClaim = await secondReplica.Set<OutboxState>()
                .FromSqlRaw(secondClaimSql)
                .AsTracking()
                .SingleAsync(timeout.Token);
            Assert.Equal(outboxId, secondClaim.OutboxId);
            Assert.Equal(firstLockId, secondClaim.LockId);

            var secondLockId = Guid.NewGuid();
            secondClaim.LockId = secondLockId;
            await secondReplica.SaveChangesAsync(timeout.Token);
            await secondTransaction.CommitAsync(timeout.Token);

            await using var verification = createContext(databaseConnection);
            var persisted = await verification.Set<OutboxState>()
                .AsNoTracking()
                .SingleAsync(state => state.OutboxId == outboxId, timeout.Token);
            Assert.Equal(secondLockId, persisted.LockId);
        }
        finally
        {
            if (databaseCreated)
            {
                await ExecuteAdministrativeCommand(
                    configuredConnection!,
                    $"DROP DATABASE `{databaseName}`;",
                    CancellationToken.None);
            }
        }
    }

    private static string WithDatabase(string connectionString, string databaseName)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            Database = databaseName,
        };
        return builder.ConnectionString;
    }

    private static async Task ExecuteAdministrativeCommand(
        string connectionString,
        string commandText,
        CancellationToken cancellationToken)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            Database = string.Empty,
        };
        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText, connection)
        {
            CommandTimeout = 15,
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
