namespace MySqlMigrations;

/// <summary>
/// Marker interface for declarative MySQL-migration configuration types.
/// <para>
/// Implementers expose two well-known <c>public const string</c> fields that
/// the host uses to locate and apply raw-SQL migrations at startup:
/// <list type="bullet">
///   <item><c>ConnectionStringName</c> – environment variable / configuration key
///         holding the target connection string.</item>
///   <item><c>MigrationsPath</c> – relative path (under the running app's working
///         directory) where the <c>Migrations/*.sql</c> files live.</item>
/// </list>
/// </para>
/// <example>
/// <code>
/// public class ServiceMigrations : IMigrationsConfiguration
/// {
///     public const string ConnectionStringName = "SQL_CONNECTION_STRING";
///     public const string MigrationsPath        = "MyService.Infraestructure/Database/Sql";
/// }
/// </code>
/// </example>
/// </summary>
public interface IMigrationsConfiguration
{
}
