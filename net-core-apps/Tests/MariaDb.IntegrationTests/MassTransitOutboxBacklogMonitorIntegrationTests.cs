using Feed.Infra.Persistence;
using MassTransit.EntityFrameworkCoreIntegration;
using MessageBrokerExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Xunit;

namespace MariaDb.IntegrationTests;

public sealed class MassTransitOutboxBacklogMonitorIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Aggregate_sampler_reports_depth_and_oldest_age_then_returns_to_zero()
    {
        var configuredConnection = Environment.GetEnvironmentVariable("TEST_MARIADB_CONNECTION");
        if (string.IsNullOrWhiteSpace(configuredConnection))
            Assert.Skip("Set TEST_MARIADB_CONNECTION to run the isolated MariaDB integration suite.");

        var databaseName = $"mt_outbox_monitor_{Guid.NewGuid():N}";
        Assert.StartsWith("mt_outbox_monitor_", databaseName, StringComparison.Ordinal);
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

            var contextOptions = new DbContextOptionsBuilder<FeedContext>()
                .UseMySql(
                    databaseConnection,
                    new MariaDbServerVersion(new Version(10, 6, 0)),
                    provider => provider.CommandTimeout(10))
                .Options;

            await using (var migrationContext = new FeedContext(contextOptions))
                await migrationContext.Database.MigrateAsync(timeout.Token);

            var services = new ServiceCollection();
            services.AddScoped(_ => new FeedContext(contextOptions));
            await using var serviceProvider = services.BuildServiceProvider();
            var configuredOptions = new MassTransitBrokerOptions();
            configuredOptions.OutboxTelemetry.MaximumPendingMessages = 1;
            var brokerOptions = Options.Create(configuredOptions);
            var sampler = new MassTransitOutboxBacklogSampler<FeedContext>(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                brokerOptions);
            using var monitor = new MassTransitOutboxBacklogMonitor<FeedContext>(
                sampler,
                brokerOptions,
                TimeProvider.System,
                NullLogger<MassTransitOutboxBacklogMonitor<FeedContext>>.Instance);

            await monitor.SampleOnceAsync(timeout.Token);
            Assert.True(monitor.Current.SampleSucceeded);
            Assert.Equal(0, monitor.Current.PendingMessages);
            Assert.Equal(0, monitor.Current.OldestMessageAgeSeconds);

            await using (var insertContext = new FeedContext(contextOptions))
            {
                insertContext.Set<OutboxMessage>().Add(new OutboxMessage
                {
                    MessageId = Guid.NewGuid(),
                    ContentType = "application/json",
                    MessageType = "urn:message:monitor-probe",
                    Body = "{}",
                    SentTime = DateTime.UtcNow.AddSeconds(-45),
                });
                await insertContext.SaveChangesAsync(timeout.Token);
            }

            await monitor.SampleOnceAsync(timeout.Token);
            Assert.True(monitor.Current.SampleSucceeded);
            Assert.Equal(1, monitor.Current.PendingMessages);
            Assert.InRange(monitor.Current.OldestMessageAgeSeconds, 40, 60);

            await using (var insertContext = new FeedContext(contextOptions))
            {
                insertContext.Set<OutboxMessage>().AddRange(
                    new OutboxMessage
                    {
                        MessageId = Guid.NewGuid(),
                        ContentType = "application/json",
                        MessageType = "urn:message:monitor-probe",
                        Body = "{}",
                        SentTime = DateTime.UtcNow,
                    },
                    new OutboxMessage
                    {
                        MessageId = Guid.NewGuid(),
                        ContentType = "application/json",
                        MessageType = "urn:message:monitor-probe",
                        Body = "{}",
                        SentTime = DateTime.UtcNow,
                    });
                await insertContext.SaveChangesAsync(timeout.Token);
            }

            await monitor.SampleOnceAsync(timeout.Token);
            Assert.Equal(2, monitor.Current.PendingMessages);
            var readiness = new MassTransitOutboxBacklogHealthCheck<FeedContext>(
                monitor,
                brokerOptions,
                TimeProvider.System);
            var breached = await readiness.CheckHealthAsync(
                new HealthCheckContext(),
                timeout.Token);
            Assert.Equal(HealthStatus.Unhealthy, breached.Status);
            Assert.Contains("pending-message", breached.Description, StringComparison.Ordinal);

            await using (var deleteContext = new FeedContext(contextOptions))
                await deleteContext.Set<OutboxMessage>().ExecuteDeleteAsync(timeout.Token);

            await monitor.SampleOnceAsync(timeout.Token);
            Assert.True(monitor.Current.SampleSucceeded);
            Assert.Equal(0, monitor.Current.PendingMessages);
            Assert.Equal(0, monitor.Current.OldestMessageAgeSeconds);
        }
        finally
        {
            if (databaseCreated)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await ExecuteAdministrativeCommand(
                    configuredConnection!,
                    $"DROP DATABASE `{databaseName}`;",
                    cleanup.Token);
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
