using Inspacy.Data.MariaDb;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace MariaDbConfiguration.UnitTests;

public sealed class MariaDbConnectionPolicyTests
{
    [Theory]
    [InlineData("Api", 8)]
    [InlineData("ConsumerWorker", 6)]
    [InlineData("JobWorker", 4)]
    [InlineData("Migration", 5)]
    [InlineData("Combined", 7)]
    public void Role_budget_overrides_any_pool_size_in_the_secret(string role, int expected)
    {
        var configuration = Configuration(role, poolSize: expected);

        var policy = MariaDbConnectionPolicy.Create(configuration, "feed");
        var normalized = new MySqlConnectionStringBuilder(policy.ConnectionString);

        Assert.Equal(role, policy.RuntimeRole);
        Assert.Equal(expected, policy.MaximumPoolSize);
        Assert.Equal((uint)expected, normalized.MaximumPoolSize);
        Assert.Equal((uint)0, normalized.MinimumPoolSize);
        Assert.True(normalized.Pooling);
        Assert.True(normalized.ConnectionReset);
        Assert.False(normalized.PersistSecurityInfo);
        Assert.False(normalized.AllowLoadLocalInfile);
        Assert.Equal(MySqlSslMode.VerifyFull, normalized.SslMode);
        Assert.Equal($"inspacy-feed/{role}", normalized.ApplicationName);
    }

    [Fact]
    public void Bounded_timeouts_and_lifetime_are_applied()
    {
        var configuration = Configuration("Api", poolSize: 8, additional: new()
        {
            ["Database:ConnectionTimeoutSeconds"] = "7",
            ["Database:CommandTimeoutSeconds"] = "41",
            ["Database:ConnectionIdleTimeoutSeconds"] = "240",
            ["Database:ConnectionLifetimeSeconds"] = "600",
        });

        var policy = MariaDbConnectionPolicy.Create(configuration, "comment");
        var normalized = new MySqlConnectionStringBuilder(policy.ConnectionString);

        Assert.Equal((uint)7, normalized.ConnectionTimeout);
        Assert.Equal((uint)41, normalized.DefaultCommandTimeout);
        Assert.Equal((uint)240, normalized.ConnectionIdleTimeout);
        Assert.Equal((uint)600, normalized.ConnectionLifeTime);
        Assert.Equal(41, policy.CommandTimeoutSeconds);
    }

    [Fact]
    public void Development_may_explicitly_use_a_local_non_tls_database()
    {
        var configuration = Configuration("Api", poolSize: 8, additional: new()
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Database:RequireTls"] = "false",
            ["Database:MariaDbSqlConnection"] =
                "Server=localhost;Database=test;User ID=test;Password=test;SslMode=None;MaximumPoolSize=99",
        });

        var policy = MariaDbConnectionPolicy.Create(configuration, "comment");
        var normalized = new MySqlConnectionStringBuilder(policy.ConnectionString);

        Assert.Equal(MySqlSslMode.None, normalized.SslMode);
        Assert.Equal((uint)8, normalized.MaximumPoolSize);
    }

    [Theory]
    [InlineData("{{secret:DatabaseConnectionString}}", "non-placeholder secret")]
    [InlineData("", "non-placeholder secret")]
    public void Unresolved_or_empty_secrets_are_rejected(string connectionString, string message)
    {
        var configuration = Configuration("Api", 8, new()
        {
            ["Database:MariaDbSqlConnection"] = connectionString,
        });

        var error = Assert.Throws<InvalidOperationException>(
            () => MariaDbConnectionPolicy.Create(configuration, "comment"));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_role_budget_is_rejected()
    {
        var configuration = Configuration("Api", 8);
        configuration = new ConfigurationBuilder().AddInMemoryCollection(
            configuration.AsEnumerable()
                .Where(item => item.Key != "Database:PoolSizes:Api")
                .ToDictionary(item => item.Key, item => item.Value)).Build();

        var error = Assert.Throws<InvalidOperationException>(
            () => MariaDbConnectionPolicy.Create(configuration, "comment"));

        Assert.Contains("Database:PoolSizes:Api is required", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Api", "0", "from 1 through 64")]
    [InlineData("Api", "65", "from 1 through 64")]
    [InlineData("Unknown", "8", "Unsupported Runtime:Role")]
    public void Invalid_role_or_pool_configuration_fails_startup(
        string role,
        string poolSize,
        string message)
    {
        var configuration = Configuration(role, 8, new()
        {
            [$"Database:PoolSizes:{role}"] = poolSize,
        });

        var error = Assert.Throws<InvalidOperationException>(
            () => MariaDbConnectionPolicy.Create(configuration, "comment"));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_cannot_disable_database_tls()
    {
        var configuration = Configuration("Api", 8, new()
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["Database:RequireTls"] = "false",
        });

        var error = Assert.Throws<InvalidOperationException>(
            () => MariaDbConnectionPolicy.Create(configuration, "comment"));

        Assert.Contains("only in Development", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("../../comment")]
    public void Invalid_application_name_is_rejected(string serviceName)
    {
        Assert.Throws<ArgumentException>(
            () => MariaDbConnectionPolicy.Create(Configuration("Api", 8), serviceName));
    }

    private static IConfigurationRoot Configuration(
        string role,
        int poolSize,
        Dictionary<string, string?>? additional = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Runtime:Role"] = role,
            [$"Database:PoolSizes:{role}"] = poolSize.ToString(),
            ["Database:MariaDbSqlConnection"] =
                "Server=db.example;Database=test;User ID=test;Password=test;SslMode=None;MaximumPoolSize=99",
        };
        foreach (var (key, value) in additional ?? [])
            values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
