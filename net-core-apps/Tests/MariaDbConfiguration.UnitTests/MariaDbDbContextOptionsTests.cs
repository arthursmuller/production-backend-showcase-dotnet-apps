using Inspacy.Data.MariaDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Xunit;

namespace MariaDbConfiguration.UnitTests;

public sealed class MariaDbDbContextOptionsTests
{
    private const string ConnectionString =
        "Server=db.example;Database=test;User ID=test;Password=test;SslMode=Required";

    [Fact]
    public void Runtime_options_apply_timeout_retry_policy_and_registered_telemetry()
    {
        var services = new ServiceCollection();
        var telemetry = new MariaDbCommandTelemetryInterceptor(
            new MariaDbCommandTelemetryOptions(250));
        services.AddSingleton(telemetry);
        using var serviceProvider = services.BuildServiceProvider();
        var connection = new MariaDbRuntimeConnection(
            ConnectionString,
            RuntimeRole: "Api",
            MaximumPoolSize: 8,
            CommandTimeoutSeconds: 41);
        var builder = new DbContextOptionsBuilder<TestContext>();

        builder.UseMariaDbRuntime(connection, serviceProvider);

        using var context = new TestContext(builder.Options);
        AssertConnectionString(context);
        Assert.Equal(41, context.Database.GetCommandTimeout());
        AssertRetryPolicy(context);

        var coreOptions = builder.Options.FindExtension<CoreOptionsExtension>();
        Assert.NotNull(coreOptions);
        Assert.Contains(telemetry, coreOptions.Interceptors!);
    }

    [Fact]
    public void Design_time_options_read_the_documented_variable_and_apply_provider_policy()
    {
        string? requestedVariable = null;

        var options = MariaDbDesignTimeOptions.Create<TestContext>(variable =>
        {
            requestedVariable = variable;
            return ConnectionString;
        });

        using var context = new TestContext(options);
        Assert.Equal(
            MariaDbDesignTimeOptions.ConnectionStringEnvironmentVariable,
            requestedVariable);
        AssertConnectionString(context);
        Assert.Equal(30, context.Database.GetCommandTimeout());
        AssertRetryPolicy(context);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{{secret:DatabaseConnectionString}}")]
    public void Design_time_options_reject_missing_or_unresolved_secrets(
        string? connectionString)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            MariaDbDesignTimeOptions.Create<TestContext>(_ => connectionString));

        Assert.Contains(
            MariaDbDesignTimeOptions.ConnectionStringEnvironmentVariable,
            error.Message,
            StringComparison.Ordinal);
    }

    private static void AssertRetryPolicy(TestContext context)
    {
        var strategy = Assert.IsAssignableFrom<ExecutionStrategy>(
            context.Database.CreateExecutionStrategy());
        Assert.True(strategy.RetriesOnFailure);
        Assert.Equal(3, strategy.MaxRetryCount);
        Assert.Equal(TimeSpan.FromSeconds(2), strategy.MaxRetryDelay);
    }

    private static void AssertConnectionString(TestContext context)
    {
        var expected = new MySqlConnectionStringBuilder(ConnectionString);
        var configured = context.Database.GetConnectionString();
        Assert.NotNull(configured);
        var actual = new MySqlConnectionStringBuilder(configured);
        Assert.Equal(expected.Server, actual.Server);
        Assert.Equal(expected.Database, actual.Database);
        Assert.Equal(expected.UserID, actual.UserID);
        Assert.Equal(expected.Password, actual.Password);
        Assert.Equal(expected.SslMode, actual.SslMode);
    }

    private sealed class TestContext(DbContextOptions<TestContext> options)
        : DbContext(options);
}
