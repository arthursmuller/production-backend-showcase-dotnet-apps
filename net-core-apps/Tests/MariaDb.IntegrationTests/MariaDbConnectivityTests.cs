using MySqlConnector;
using Xunit;

namespace MariaDb.IntegrationTests;

public sealed class MariaDbConnectivityTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Configured_server_is_MariaDb_10_6_or_newer()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_MARIADB_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip(
                "Set TEST_MARIADB_CONNECTION to run the isolated MariaDB integration suite.");
        }

        await using var connection = new MySqlConnection(connectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await connection.OpenAsync(timeout.Token);
        await using var command = new MySqlCommand("SELECT VERSION()", connection)
        {
            CommandTimeout = 10,
        };
        var version = Convert.ToString(await command.ExecuteScalarAsync(timeout.Token));

        Assert.Contains("MariaDB", version, StringComparison.OrdinalIgnoreCase);
        Assert.True(ParseMajorMinor(version!) >= new Version(10, 6), $"Unsupported server: {version}");
    }

    private static Version ParseMajorMinor(string serverVersion)
    {
        var numericPrefix = new string(serverVersion
            .TakeWhile(character => char.IsDigit(character) || character == '.')
            .ToArray());
        var parts = numericPrefix.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return new Version(int.Parse(parts[0]), int.Parse(parts[1]));
    }
}
