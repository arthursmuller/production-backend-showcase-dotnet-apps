using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Architecture.Tests;

public sealed class SchemaBaselineTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Sanitized_ef_schema_baseline_is_deterministic_and_current()
    {
        var manifest = BuildManifest();
        var baselinePath = Path.Combine(
            RepositoryRoot,
            "Tests",
            "Architecture.Tests",
            "Baselines",
            "ef-schema-contract-manifest.json");

        if (string.Equals(
                Environment.GetEnvironmentVariable("UPDATE_EF_SCHEMA_BASELINE"),
                "1",
                StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            File.WriteAllText(baselinePath, manifest, new UTF8Encoding(false));
            return;
        }

        Assert.True(File.Exists(baselinePath), $"Missing EF schema baseline: {baselinePath}");
        Assert.Equal(
            File.ReadAllText(baselinePath).Replace("\r\n", "\n", StringComparison.Ordinal),
            manifest);
    }

    private static string BuildManifest()
    {
        var services = new[]
        {
            Describe("Comment", "CommentContext", Path.Combine(
                "Services", "Comment", "Comment.Infraestructure", "Database", "Ef", "Migrations"),
                "CommentContextModelSnapshot.cs"),
            Describe("Feed", "FeedContext", Path.Combine(
                "Services", "Feed", "Feed.Infraestructure", "Database", "Ef", "Migrations"),
                "FeedContextModelSnapshot.cs"),
            Describe("Following", "FollowingContext", Path.Combine(
                "Services", "Following", "Following.Infraestructure", "Database", "Ef", "Migrations"),
                "FollowingContextModelSnapshot.cs"),
            Describe("Interaction", "InteractionDbContext", Path.Combine(
                "Services", "Interaction", "Interaction.Infraestructure", "Database", "Ef", "Migrations"),
                "InteractionDbContextModelSnapshot.cs"),
            Describe("Moderation", "ModerationDbContext", Path.Combine(
                "Services", "Moderation", "Moderation.Infraestructure", "Database", "Ef", "Migrations"),
                "ModerationDbContextModelSnapshot.cs"),
        };
        var fleetContract = string.Join(
            "\n---\n",
            services.Select(service =>
                $"{service.Service}:{service.Context}:{service.LatestMigrationId}:" +
                $"{service.ModelSnapshotSha256}:{service.MigrationSourcesSha256}:" +
                string.Join(',', service.Tables) + ":" + string.Join(',', service.Indexes)));
        var document = new EfSchemaBaseline(
            FormatVersion: 1,
            RegenerationCommand:
                "pwsh -File Tests/Architecture.Tests/Baselines/regenerate-ef-schema-baseline.ps1",
            Scope: "five-service-ef-model-and-migration-chain",
            FleetSchemaSha256: Sha256(fleetContract),
            Services: services);
        return JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static EfServiceSchema Describe(
        string service,
        string context,
        string relativeMigrationDirectory,
        string snapshotFileName)
    {
        var directory = Path.Combine(RepositoryRoot, relativeMigrationDirectory);
        var snapshotPath = Path.Combine(directory, snapshotFileName);
        Assert.True(File.Exists(snapshotPath), $"Missing model snapshot: {snapshotPath}");
        var snapshot = Normalize(File.ReadAllText(snapshotPath));

        var migrationDeclarations = Directory.EnumerateFiles(directory, "*.cs")
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .SelectMany(path => Regex.Matches(
                File.ReadAllText(path),
                "\\[Migration\\(\"(?<id>[0-9]+_[A-Za-z0-9_]+)\"\\)\\]",
                RegexOptions.CultureInvariant)
                .Select(match => (Path: Path.GetFileName(path), Id: match.Groups["id"].Value)))
            .ToArray();
        var duplicateMigrationIds = migrationDeclarations
            .GroupBy(value => value.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(value => value.Path))}")
            .ToArray();
        Assert.True(
            duplicateMigrationIds.Length == 0,
            $"Duplicate migration ids: {string.Join("; ", duplicateMigrationIds)}");

        var migrationIds = migrationDeclarations
            .Select(value => value.Id)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(migrationIds);

        var migrationSources = Directory.EnumerateFiles(directory, "*.cs")
            .Where(path => !path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(snapshotFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .Select(path => $"{Path.GetFileName(path)}\n{Normalize(File.ReadAllText(path))}")
            .ToArray();
        var entities = Captures(snapshot, "modelBuilder\\.Entity\\(\"(?<value>[^\"]+)\"");
        var tables = Captures(snapshot, "\\.ToTable\\(\"(?<value>[^\"]+)\"");
        var indexes = Captures(snapshot, "\\.HasDatabaseName\\(\"(?<value>[^\"]+)\"\\)");

        return new EfServiceSchema(
            service,
            context,
            migrationIds.Length,
            migrationIds[^1],
            migrationIds,
            entities.Length,
            tables,
            indexes,
            Sha256(snapshot),
            Sha256(string.Join("\n---\n", migrationSources)));
    }

    private static string[] Captures(string source, string expression) =>
        Regex.Matches(source, expression, RegexOptions.CultureInvariant)
            .Select(match => match.Groups["value"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the net-core-apps repository root.");
    }
}

internal sealed record EfServiceSchema(
    string Service,
    string Context,
    int MigrationCount,
    string LatestMigrationId,
    IReadOnlyList<string> MigrationIds,
    int EntityCount,
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> Indexes,
    string ModelSnapshotSha256,
    string MigrationSourcesSha256);

internal sealed record EfSchemaBaseline(
    int FormatVersion,
    string RegenerationCommand,
    string Scope,
    string FleetSchemaSha256,
    IReadOnlyList<EfServiceSchema> Services);
