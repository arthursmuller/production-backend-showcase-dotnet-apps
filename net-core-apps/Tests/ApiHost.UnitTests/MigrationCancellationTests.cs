using EfCoreMigrations;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlMigrations;
using Xunit;

namespace ApiHost.UnitTests;

public sealed class MigrationCancellationTests
{
    [Fact]
    public async Task Cancellation_during_retry_delay_aborts_promptly_without_a_later_attempt()
    {
        var attempts = 0;
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(25));

        var migration = EfCoreMigrationsExtensions.ExecuteMigrationWithRetryAsync(
            _ =>
            {
                Interlocked.Increment(ref attempts);
                return Task.FromException(new InvalidOperationException("database unavailable"));
            },
            underK8s: false,
            NullLogger.Instance,
            "CancellationProbeContext",
            cancellation.Token);

        var timeout = Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Same(migration, await Task.WhenAny(migration, timeout));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => migration);

        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        Assert.Equal(1, Volatile.Read(ref attempts));
    }

    [Fact]
    public async Task Raw_migration_orchestration_stops_before_later_mutation_after_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new RecordingMigrationService(cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.MigrateAsync(cancellation.Token));

        await Task.Delay(TimeSpan.FromMilliseconds(25), TestContext.Current.CancellationToken);
        Assert.Equal(["create"], service.Operations);
    }

    [Fact]
    public async Task K8s_migration_failure_is_not_retried()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EfCoreMigrationsExtensions.ExecuteMigrationWithRetryAsync(
                _ =>
                {
                    Interlocked.Increment(ref attempts);
                    return Task.FromException(new InvalidOperationException("database unavailable"));
                },
                underK8s: true,
                NullLogger.Instance,
                "K8sProbeContext",
                TestContext.Current.CancellationToken));

        Assert.Equal(1, attempts);
    }

    private sealed class RecordingMigrationService(CancellationTokenSource cancellation)
        : DatabaseMigrationService(
            "Server=127.0.0.1;Database=cancellation_probe;User ID=test;Password=test;",
            "unused",
            "cancellation_probe",
            NullLogger<DatabaseMigrationService>.Instance)
    {
        public List<string> Operations { get; } = [];

        public override Task EnsureDatabaseCreatedAsync(CancellationToken cancellationToken)
        {
            Operations.Add("create");
            cancellation.Cancel();
            return Task.CompletedTask;
        }

        public override Task ApplyPendingMigrationsAsync(CancellationToken cancellationToken)
        {
            Operations.Add("pending");
            return Task.CompletedTask;
        }

        public override Task ApplyProcedureMigrationsAsync(CancellationToken cancellationToken)
        {
            Operations.Add("procedures");
            return Task.CompletedTask;
        }

        public override Task EnsureDatabaseDeletedAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
