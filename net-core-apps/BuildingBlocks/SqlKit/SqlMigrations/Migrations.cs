using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace MySqlMigrations;
/// <summary>
/// Interface for database migration operations
/// </summary>
public interface IDatabaseMigrationService
{
    Task Migrate();
    Task EnsureDatabaseCreated();
    Task EnsureDatabaseDeleted();
    Task ApplyPendingMigrations();
    Task ApplyProcedureMigrations();
}

/// <summary>
/// Service responsible for applying MySQL database migrations
/// </summary>
public class DatabaseMigrationService : IDatabaseMigrationService
{
    private readonly string _connectionString;
    private readonly string _sqlPath;
    private readonly string _dbName;
    private readonly ILogger<DatabaseMigrationService> _logger;
    private readonly string _migrationsTableName = "__MigrationHistory";
    private readonly Func<string, string> _pathResolver;

    public DatabaseMigrationService(
        string connectionString,
        string sqlPath,
        string dbName,
        ILogger<DatabaseMigrationService> logger,
        Func<string, string> pathResolver = null)
    {
        _connectionString = connectionString;
        _sqlPath = sqlPath;
        _dbName = dbName;
        _logger = logger;
        _pathResolver = pathResolver ?? GetDefaultDirectoryPath;
    }

    public async Task Migrate()
    {
        await EnsureDatabaseCreated();
        await ApplyPendingMigrations();
        await ApplyProcedureMigrations();
    }

    /// <summary>
    /// Ensures the target database exists
    /// </summary>
    public async Task EnsureDatabaseCreated()
    {
        var builder = new MySqlConnectionStringBuilder(_connectionString);
        var databaseName = builder.Database;
        builder.Database = null;

        _logger.LogInformation($"Ensuring database {databaseName} exists");

        using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{databaseName}`;";
        await command.ExecuteNonQueryAsync();

        _logger.LogInformation($"Database {databaseName} created or already exists");
    }

    /// <summary>
    /// Applies any pending migrations that haven't been executed yet
    /// </summary>
    public async Task ApplyPendingMigrations()
    {
        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await EnsureMigrationTableExists(connection);

        var appliedMigrations = await GetAppliedMigrations(connection);

        var migrationsPath = _pathResolver(_sqlPath + "/Migrations");
        _logger.LogInformation($"Looking for migrations in: {migrationsPath}");

        var migrationFiles = Directory.Exists(migrationsPath)
            ? Directory.GetFiles(migrationsPath, "*.sql")
                .Where(f => !f.Contains(".procedure.sql"))
                .OrderBy(f => f)
                .ToList()
            : new List<string>();

        if (migrationFiles.Count == 0)
        {
            _logger.LogWarning($"No migration files found in {migrationsPath}");
        }
        else
        {
            _logger.LogInformation($"Found {migrationFiles.Count} migration files");
        }

        foreach (var migrationFile in migrationFiles)
        {
            var fileName = Path.GetFileName(migrationFile);

            if (!appliedMigrations.Contains(fileName))
            {
                _logger.LogInformation($"Applying migration: {fileName}");

                var sql = await File.ReadAllTextAsync(migrationFile);
                sql = ReplaceDatabaseName(sql);

                await ExecuteMigrationScript(connection, sql, fileName);

                _logger.LogInformation($"Migration {fileName} applied successfully");
            }
            else
            {
                _logger.LogInformation($"Migration {fileName} already applied, skipping");
            }
        }
    }

    /// <summary>
    /// Applies all procedure migrations (these are always reapplied)
    /// </summary>
    public async Task ApplyProcedureMigrations()
    {
        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        // Get procedure files from procedures subdirectory
        var proceduresPath = _pathResolver(_sqlPath + "/Migrations/Procedures");
        _logger.LogInformation($"Looking for procedures in: {proceduresPath}");

        var procedureFiles = new List<string>();

        if (Directory.Exists(proceduresPath))
        {
            procedureFiles.AddRange(
                Directory.GetFiles(proceduresPath, "*.procedure.sql")
                    .OrderBy(f => f)
            );
        }

        // Also look for procedure files in the main migrations directory
        var migrationsPath = _pathResolver(_sqlPath + "/Migrations");

        if (Directory.Exists(migrationsPath))
        {
            procedureFiles.AddRange(
                Directory.GetFiles(migrationsPath, "*.procedure.sql")
                    .OrderBy(f => f)
            );
        }

        if (procedureFiles.Count == 0)
        {
            _logger.LogWarning("No procedure files found");
        }
        else
        {
            _logger.LogInformation($"Found {procedureFiles.Count} procedure files");
        }

        foreach (var procedureFile in procedureFiles)
        {
            var fileName = Path.GetFileName(procedureFile);
            _logger.LogInformation($"Applying procedure: {fileName}");

            var sql = await File.ReadAllTextAsync(procedureFile);
            sql = ReplaceDatabaseName(sql);

            await ExecuteProcedureScript(connection, sql);

            _logger.LogInformation($"Procedure {fileName} applied successfully");
        }
    }

    /// <summary>
    /// Ensures the migration history table exists
    /// </summary>
    private async Task EnsureMigrationTableExists(MySqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $@"
            CREATE TABLE IF NOT EXISTS `{_migrationsTableName}` (
                `MigrationId` VARCHAR(255) NOT NULL,
                `AppliedOn` DATETIME NOT NULL,
                PRIMARY KEY (`MigrationId`)
            );";

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Gets a list of all migrations that have been applied
    /// </summary>
    private async Task<HashSet<string>> GetAppliedMigrations(MySqlConnection connection)
    {
        var appliedMigrations = new HashSet<string>();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT `MigrationId` FROM `{_migrationsTableName}`;";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            appliedMigrations.Add(reader.GetString(0));
        }

        return appliedMigrations;
    }

    /// <summary>
    /// Executes a migration script and records it in the migration history
    /// </summary>
    private async Task ExecuteMigrationScript(MySqlConnection connection, string sql, string migrationId)
    {
        using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // Execute the migration script
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.Transaction = transaction;
                await command.ExecuteNonQueryAsync();
            }

            // Record the migration in history
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"INSERT INTO `{_migrationsTableName}` (`MigrationId`, `AppliedOn`) VALUES (@id, NOW());";
                command.Parameters.AddWithValue("@id", migrationId);
                command.Transaction = transaction;
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, $"Error applying migration {migrationId}");
            throw;
        }
    }

    /// <summary>
    /// Executes a procedure script with appropriate delimiter handling
    /// </summary>
    private async Task ExecuteProcedureScript(MySqlConnection connection, string sql)
    {
        using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // For stored procedures, execute statements one by one
            // Split by statement delimiters (typically at the end of procedure definitions)
            var statements = Regex.Split(sql, @"(?<=\bEND\s*;)", RegexOptions.IgnoreCase)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            foreach (var statement in statements)
            {
                using var command = connection.CreateCommand();
                command.CommandText = statement;
                command.Transaction = transaction;
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error applying procedure");
            throw;
        }
    }

    public async Task EnsureDatabaseDeleted()
    {
        var builder = new MySqlConnectionStringBuilder(_connectionString);
        var server = builder.Server;
        var port = builder.Port;
        var user = builder.UserID;
        var password = builder.Password;

        _logger.LogInformation($"Ensuring database {_dbName} is deleted by killing connections");

        using (var connection = new MySqlConnection($"Server={server};Port={port};User ID={user};Password={password};"))
        {
            await connection.OpenAsync();

            // Kill all active connections to the target database
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT GROUP_CONCAT(CONCAT('KILL ', id, ';')) FROM information_schema.processlist WHERE db = '{_dbName}';";
                var killCommands = await command.ExecuteScalarAsync() as string;

                if (!string.IsNullOrEmpty(killCommands))
                {
                    var individualKillCommands = killCommands.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var killCommand in individualKillCommands)
                    {
                        using (var killCmd = connection.CreateCommand())
                        {
                            killCmd.CommandText = killCommand.Replace(",", "");
                            await killCmd.ExecuteNonQueryAsync();
                            _logger.LogInformation($"Executed: {killCommand}");
                        }
                    }
                    _logger.LogInformation($"Successfully killed all connections to database {_dbName}");
                }
                else
                {
                    _logger.LogInformation($"No active connections found for database {_dbName}");
                }
            }

            // Now attempt to drop the database
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"DROP DATABASE IF EXISTS `{_dbName}`;";
                await command.ExecuteNonQueryAsync();
                _logger.LogInformation($"Database {_dbName} deleted or did not exist");
            }
        }
    }
    /// <summary>
    /// Replaces database name in SQL script with the configured database name
    /// </summary>
    private string ReplaceDatabaseName(string sql)
    {
        // Look for database name in CREATE DATABASE or USE statements
        var createDbMatch = Regex.Match(sql, @"CREATE\s+DATABASE\s+(?:IF\s+NOT\s+EXISTS\s+)?[`'""]?(\w+)[`'""]?", RegexOptions.IgnoreCase);
        var useDbMatch = Regex.Match(sql, @"USE\s+[`'""]?(\w+)[`'""]?", RegexOptions.IgnoreCase);

        string originalDbName = null;

        if (createDbMatch.Success)
        {
            originalDbName = createDbMatch.Groups[1].Value;
        }
        else if (useDbMatch.Success)
        {
            originalDbName = useDbMatch.Groups[1].Value;
        }

        if (originalDbName != null && originalDbName != _dbName)
        {
            // Replace database name in CREATE DATABASE statements
            sql = Regex.Replace(
                sql,
                $@"CREATE\s+DATABASE\s+(?:IF\s+NOT\s+EXISTS\s+)?[`'""]?{originalDbName}[`'""]?",
                $"CREATE DATABASE IF NOT EXISTS `{_dbName}`",
                RegexOptions.IgnoreCase
            );

            // Replace database name in USE statements
            sql = Regex.Replace(
                sql,
                $@"USE\s+[`'""]?{originalDbName}[`'""]?",
                $"USE `{_dbName}`",
                RegexOptions.IgnoreCase
            );

            // Replace database name in fully qualified table references
            sql = Regex.Replace(
                sql,
                $@"[`'""]?{originalDbName}[`'""]?\.[`'""]?(\w+)[`'""]?",
                $"`{_dbName}`.`$1`",
                RegexOptions.IgnoreCase
            );
        }



        return sql;
    }

    /// <summary>
    /// Default directory path resolver
    /// </summary>
    private string GetDefaultDirectoryPath(string relativePath)
    {
        var parentDir = Directory.GetParent(Environment.CurrentDirectory)?.FullName;
        var filePath = $"{parentDir}/{relativePath}";
        var dir = parentDir?.Length > 1 ? filePath : relativePath;
        _logger.LogWarning(parentDir);
        _logger.LogWarning(relativePath);
        return dir;
    }
}
