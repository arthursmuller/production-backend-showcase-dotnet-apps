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
    Task MigrateAsync(CancellationToken cancellationToken);
    Task EnsureDatabaseCreatedAsync(CancellationToken cancellationToken);
    Task EnsureDatabaseDeletedAsync(CancellationToken cancellationToken);
    Task ApplyPendingMigrationsAsync(CancellationToken cancellationToken);
    Task ApplyProcedureMigrationsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Service responsible for applying MySQL database migrations
/// </summary>
public class DatabaseMigrationService : IDatabaseMigrationService
{
    private static readonly TimeSpan RollbackTimeout = TimeSpan.FromSeconds(10);
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
        Func<string, string>? pathResolver = null)
    {
        _connectionString = connectionString;
        _sqlPath = sqlPath;
        _dbName = dbName;
        _logger = logger;
        _pathResolver = pathResolver ?? GetDefaultDirectoryPath;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureDatabaseCreatedAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await ApplyPendingMigrationsAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await ApplyProcedureMigrationsAsync(cancellationToken);
    }

    /// <summary>
    /// Ensures the target database exists
    /// </summary>
    public virtual async Task EnsureDatabaseCreatedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var builder = new MySqlConnectionStringBuilder(_connectionString);
        var databaseName = builder.Database;
        builder.Database = null;

        _logger.LogInformation("Ensuring database {DatabaseName} exists", databaseName);

        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{databaseName}`;";
        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Database {DatabaseName} created or already exists", databaseName);
    }

    /// <summary>
    /// Applies any pending migrations that haven't been executed yet
    /// </summary>
    public virtual async Task ApplyPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await EnsureMigrationTableExistsAsync(connection, cancellationToken);

        var appliedMigrations = await GetAppliedMigrationsAsync(connection, cancellationToken);

        var migrationsPath = _pathResolver(_sqlPath + "/Migrations");
        _logger.LogInformation("Looking for migrations in: {MigrationsPath}", migrationsPath);

        var migrationFiles = Directory.Exists(migrationsPath)
            ? Directory.GetFiles(migrationsPath, "*.sql")
                .Where(f => !f.Contains(".procedure.sql"))
                .OrderBy(f => f)
                .ToList()
            : new List<string>();

        if (migrationFiles.Count == 0)
        {
            _logger.LogWarning("No migration files found in {MigrationsPath}", migrationsPath);
        }
        else
        {
            _logger.LogInformation("Found {MigrationCount} migration files", migrationFiles.Count);
        }

        foreach (var migrationFile in migrationFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(migrationFile);

            if (!appliedMigrations.Contains(fileName))
            {
                _logger.LogInformation("Applying migration: {MigrationFile}", fileName);

                var sql = await File.ReadAllTextAsync(migrationFile, cancellationToken);
                sql = ReplaceDatabaseName(sql);

                await ExecuteMigrationScriptAsync(connection, sql, fileName, cancellationToken);

                _logger.LogInformation("Migration {MigrationFile} applied successfully", fileName);
            }
            else
            {
                _logger.LogInformation("Migration {MigrationFile} already applied, skipping", fileName);
            }
        }
    }

    /// <summary>
    /// Applies all procedure migrations (these are always reapplied)
    /// </summary>
    public virtual async Task ApplyProcedureMigrationsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Get procedure files from procedures subdirectory
        var proceduresPath = _pathResolver(_sqlPath + "/Migrations/Procedures");
        _logger.LogInformation("Looking for procedures in: {ProceduresPath}", proceduresPath);

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
            _logger.LogInformation("Found {ProcedureCount} procedure files", procedureFiles.Count);
        }

        foreach (var procedureFile in procedureFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(procedureFile);
            _logger.LogInformation("Applying procedure: {ProcedureFile}", fileName);

            var sql = await File.ReadAllTextAsync(procedureFile, cancellationToken);
            sql = ReplaceDatabaseName(sql);

            await ExecuteProcedureScriptAsync(connection, sql, cancellationToken);

            _logger.LogInformation("Procedure {ProcedureFile} applied successfully", fileName);
        }
    }

    /// <summary>
    /// Ensures the migration history table exists
    /// </summary>
    private async Task EnsureMigrationTableExistsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $@"
            CREATE TABLE IF NOT EXISTS `{_migrationsTableName}` (
                `MigrationId` VARCHAR(255) NOT NULL,
                `AppliedOn` DATETIME NOT NULL,
                PRIMARY KEY (`MigrationId`)
            );";

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a list of all migrations that have been applied
    /// </summary>
    private async Task<HashSet<string>> GetAppliedMigrationsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        var appliedMigrations = new HashSet<string>();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT `MigrationId` FROM `{_migrationsTableName}`;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            appliedMigrations.Add(reader.GetString(0));
        }

        return appliedMigrations;
    }

    /// <summary>
    /// Executes a migration script and records it in the migration history
    /// </summary>
    private async Task ExecuteMigrationScriptAsync(
        MySqlConnection connection,
        string sql,
        string migrationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Execute the migration script
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.Transaction = transaction;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            // Record the migration in history
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = $"INSERT INTO `{_migrationsTableName}` (`MigrationId`, `AppliedOn`) VALUES (@id, NOW());";
                command.Parameters.AddWithValue("@id", migrationId);
                command.Transaction = transaction;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await TryRollbackAsync(transaction, migrationId);
            _logger.LogError(ex, "Error applying migration {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Executes a procedure script with appropriate delimiter handling
    /// </summary>
    private async Task ExecuteProcedureScriptAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // For stored procedures, execute statements one by one
            // Split by statement delimiters (typically at the end of procedure definitions)
            var statements = Regex.Split(sql, @"(?<=\bEND\s*;)", RegexOptions.IgnoreCase)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            foreach (var statement in statements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var command = connection.CreateCommand();
                command.CommandText = statement;
                command.Transaction = transaction;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await TryRollbackAsync(transaction, "stored procedures");
            _logger.LogError(ex, "Error applying procedure");
            throw;
        }
    }

    public virtual async Task EnsureDatabaseDeletedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var builder = new MySqlConnectionStringBuilder(_connectionString);
        var server = builder.Server;
        var port = builder.Port;
        var user = builder.UserID;
        var password = builder.Password;

        _logger.LogInformation(
            "Ensuring database {DatabaseName} is deleted by killing connections",
            _dbName);

        await using (var connection = new MySqlConnection($"Server={server};Port={port};User ID={user};Password={password};"))
        {
            await connection.OpenAsync(cancellationToken);

            // Kill all active connections to the target database
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT GROUP_CONCAT(CONCAT('KILL ', id, ';')) FROM information_schema.processlist WHERE db = @databaseName;";
                command.Parameters.AddWithValue("@databaseName", _dbName);
                var killCommands = await command.ExecuteScalarAsync(cancellationToken) as string;

                if (!string.IsNullOrEmpty(killCommands))
                {
                    var individualKillCommands = killCommands.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var killCommand in individualKillCommands)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await using (var killCmd = connection.CreateCommand())
                        {
                            killCmd.CommandText = killCommand.Replace(",", "");
                            await killCmd.ExecuteNonQueryAsync(cancellationToken);
                            _logger.LogInformation("Executed: {KillCommand}", killCommand);
                        }
                    }
                    _logger.LogInformation(
                        "Successfully killed all connections to database {DatabaseName}",
                        _dbName);
                }
                else
                {
                    _logger.LogInformation(
                        "No active connections found for database {DatabaseName}",
                        _dbName);
                }
            }

            // Now attempt to drop the database
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = $"DROP DATABASE IF EXISTS `{_dbName}`;";
                await command.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation(
                    "Database {DatabaseName} deleted or did not exist",
                    _dbName);
            }
        }
    }

    private async Task TryRollbackAsync(MySqlTransaction transaction, string operation)
    {
        // Rollback must remain possible after the caller's token is cancelled, but it must
        // never hold shutdown indefinitely. This independent, bounded token is intentional.
        using var rollbackTimeout = new CancellationTokenSource(RollbackTimeout);
        try
        {
            await transaction.RollbackAsync(rollbackTimeout.Token);
        }
        catch (Exception rollbackException)
        {
            _logger.LogWarning(
                rollbackException,
                "Rollback for {MigrationOperation} did not complete within the cleanup boundary",
                operation);
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

        string? originalDbName = null;

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
