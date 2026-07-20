using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Architecture.Tests;

public sealed class QueryPlanBaselineTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Sanitized_mariadb_query_plan_baseline_is_deterministic_and_current()
    {
        var manifest = BuildManifest();
        var baselinePath = Path.Combine(
            RepositoryRoot,
            "Tests",
            "Architecture.Tests",
            "Baselines",
            "query-plan-contract-manifest.json");

        if (string.Equals(
                Environment.GetEnvironmentVariable("UPDATE_QUERY_PLAN_BASELINE"),
                "1",
                StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            File.WriteAllText(baselinePath, manifest, new UTF8Encoding(false));
            return;
        }

        Assert.True(File.Exists(baselinePath), $"Missing query-plan baseline: {baselinePath}");
        Assert.Equal(
            File.ReadAllText(baselinePath).Replace("\r\n", "\n", StringComparison.Ordinal),
            manifest);
    }

    private static string BuildManifest()
    {
        var queries = QueryCatalog()
            .Select(Describe)
            .OrderBy(query => query.QueryId, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(5, queries.Length);
        Assert.Equal(5, queries.Select(query => query.Service).Distinct(StringComparer.Ordinal).Count());

        var fleetContract = string.Join(
            "\n",
            queries.Select(query =>
                $"{query.QueryId}:{query.ProofId}:{query.SourceTestPath}:" +
                $"{query.TestFullyQualifiedName}:{query.ExpectedIndex}:" +
                $"{query.Bounded.LimitRows}:{query.Bounded.LimitKind}:" +
                $"{string.Join(',', query.Keyset.Order)}:{query.Keyset.PredicateKind}:" +
                query.NormalizedSqlShapeSha256));
        var document = new QueryPlanBaseline(
            FormatVersion: 1,
            RegenerationCommand:
                "pwsh -File Tests/Architecture.Tests/Baselines/regenerate-query-plan-baseline.ps1",
            Scope: "five-service-real-mariadb-explain-source-contract",
            FleetQueryShapeSha256: Sha256(fleetContract),
            Queries: queries);
        return JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static QueryPlanBaselineItem Describe(QueryPlanSource source)
    {
        var testPath = Resolve(source.SourceTestPath);
        Assert.True(File.Exists(testPath), $"Missing MariaDB EXPLAIN proof: {testPath}");
        var testSource = NormalizeLineEndings(File.ReadAllText(testPath));
        AssertTestMethodExists(testSource, source.TestFullyQualifiedName, source.SourceTestPath);
        var indexAssertions = Regex.Matches(
            testSource,
            "Assert\\.(?:Equal|Contains)\\((?<arguments>[\\s\\S]*?)\\);",
            RegexOptions.CultureInvariant);
        Assert.True(
            indexAssertions.Any(match => match.Groups["arguments"].Value.Contains(
                source.ExpectedIndex,
                StringComparison.Ordinal)),
            $"The MariaDB proof does not assert index {source.ExpectedIndex} in {source.SourceTestPath}.");

        var configurationPath = Resolve(source.IndexConfigurationPath);
        Assert.True(File.Exists(configurationPath), $"Missing index configuration: {configurationPath}");
        Assert.Contains(
            $".HasDatabaseName(\"{source.ExpectedIndex}\")",
            NormalizeLineEndings(File.ReadAllText(configurationPath)),
            StringComparison.Ordinal);

        var migrationPath = Resolve(source.IndexMigrationPath);
        Assert.True(File.Exists(migrationPath), $"Missing index migration: {migrationPath}");
        var migrationSource = NormalizeLineEndings(File.ReadAllText(migrationPath));
        Assert.Contains(
            source.ExpectedIndex,
            migrationSource,
            StringComparison.Ordinal);
        var migrationFileName = Path.GetFileNameWithoutExtension(migrationPath);
        var separator = migrationFileName.IndexOf('_', StringComparison.Ordinal);
        Assert.True(separator > 0 && separator < migrationFileName.Length - 1);
        Assert.Contains(
            $"partial class {migrationFileName[(separator + 1)..]}",
            migrationSource,
            StringComparison.Ordinal);

        if (source.PageSizeOptionsPath is not null)
        {
            var pageSizeOptionsPath = Resolve(source.PageSizeOptionsPath);
            Assert.True(File.Exists(pageSizeOptionsPath), $"Missing page-size options: {pageSizeOptionsPath}");
            Assert.Contains(
                $"MaximumPageSize = {source.BoundedLimitRows - 1};",
                NormalizeLineEndings(File.ReadAllText(pageSizeOptionsPath)),
                StringComparison.Ordinal);
        }

        var sqlSource = testSource;
        if (source.SqlSourcePath is not null)
        {
            var sqlSourcePath = Resolve(source.SqlSourcePath);
            Assert.True(File.Exists(sqlSourcePath), $"Missing production SQL source: {sqlSourcePath}");
            sqlSource = NormalizeLineEndings(File.ReadAllText(sqlSourcePath));
            Assert.Contains(source.SqlConstantName!, testSource, StringComparison.Ordinal);
            Assert.Contains("EXPLAIN FORMAT=JSON", testSource, StringComparison.OrdinalIgnoreCase);
        }
        var sql = source.SqlConstantName is null
            ? ExtractExplainSql(sqlSource, source.ExplainTableName, source.SourceTestPath)
            : ExtractNamedSqlConstant(
                sqlSource,
                source.SqlConstantName,
                source.ExplainTableName,
                source.SqlSourcePath!);
        AssertLimit(
            sql,
            source.BoundedSqlLimitToken,
            source.BoundedLimitRows,
            source.BoundedProofExpression,
            testSource,
            source.SourceTestPath);
        var normalizedSqlShape = NormalizeSqlShape(sql);
        AssertKeysetContract(
            normalizedSqlShape,
            source.KeysetOrder,
            source.KeysetPredicateKind,
            source.SourceTestPath);

        return new QueryPlanBaselineItem(
            source.QueryId,
            source.ProofId,
            source.Service,
            source.SourceTestPath,
            source.TestFullyQualifiedName,
            source.ExpectedIndex,
            new BoundedQueryContract(source.BoundedLimitRows, source.BoundedLimitKind),
            new KeysetQueryContract(source.KeysetOrder, source.KeysetPredicateKind),
            Sha256(normalizedSqlShape));
    }

    private static void AssertTestMethodExists(
        string source,
        string fullyQualifiedName,
        string sourceTestPath)
    {
        var nameParts = fullyQualifiedName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(nameParts.Length >= 3, $"Invalid proof FQN {fullyQualifiedName}.");
        var methodName = nameParts[^1];
        var className = nameParts[^2];
        var namespaceName = string.Join('.', nameParts[..^2]);
        Assert.Contains($"namespace {namespaceName};", source, StringComparison.Ordinal);
        Assert.Matches(
            $@"public\s+(?:(?:sealed|partial)\s+)*class\s+{Regex.Escape(className)}\b",
            source);
        var method = Regex.Match(
            source,
            $@"public\s+(?:async\s+)?Task\s+{Regex.Escape(methodName)}\s*\(",
            RegexOptions.CultureInvariant);
        Assert.True(method.Success, $"Missing proof method {fullyQualifiedName} in {sourceTestPath}.");
        var attributeWindowStart = Math.Max(0, method.Index - 600);
        var attributeWindow = source[attributeWindowStart..method.Index];
        Assert.Contains("[Fact", attributeWindow, StringComparison.Ordinal);
    }

    private static string ExtractExplainSql(
        string source,
        string tableName,
        string sourceTestPath)
    {
        var candidates = Regex.Matches(
                source,
                "\"\"\"(?<sql>[\\s\\S]*?)\"\"\"",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups["sql"].Value.Trim())
            .Where(sql => sql.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase))
            .Where(sql => sql.Contains(tableName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.True(
            candidates.Length == 1,
            $"Expected one EXPLAIN block for {tableName} in {sourceTestPath}, found {candidates.Length}.");
        return candidates[0];
    }

    private static string ExtractNamedSqlConstant(
        string source,
        string constantName,
        string tableName,
        string sourcePath)
    {
        var match = Regex.Match(
            source,
            "\\b" + Regex.Escape(constantName) +
            "\\s*=\\s*\"\"\"(?<sql>[\\s\\S]*?)\"\"\"",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Missing SQL constant {constantName} in {sourcePath}.");
        var sql = match.Groups["sql"].Value.Trim();
        Assert.Contains(tableName, sql, StringComparison.OrdinalIgnoreCase);
        return sql;
    }

    private static string NormalizeSqlShape(string sql)
    {
        var shape = Regex.Replace(
            sql,
            "^EXPLAIN(?:\\s+FORMAT\\s*=\\s*JSON)?\\s+",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        shape = Regex.Replace(
            shape,
            "'(?:''|[^'])*'",
            "?",
            RegexOptions.CultureInvariant);
        shape = Regex.Replace(
            shape,
            "@[A-Za-z_][A-Za-z0-9_]*",
            "?",
            RegexOptions.CultureInvariant);
        shape = Regex.Replace(
            shape,
            "(?<![A-Za-z0-9_])(?:[0-9]+(?:\\.[0-9]+)?)(?![A-Za-z0-9_])",
            "?",
            RegexOptions.CultureInvariant);
        shape = shape.Replace("`", string.Empty, StringComparison.Ordinal);
        shape = Regex.Replace(shape, "\\s+", " ", RegexOptions.CultureInvariant)
            .Trim()
            .ToLowerInvariant();
        return Regex.Replace(
            shape,
            "\\s*([(),;=<>])\\s*",
            "$1",
            RegexOptions.CultureInvariant);
    }

    private static void AssertLimit(
        string sql,
        string sqlLimitToken,
        int limitRows,
        string? boundedProofExpression,
        string testSource,
        string sourceTestPath)
    {
        Assert.True(limitRows > 0, $"The bounded row limit must be positive for {sourceTestPath}.");
        var match = Regex.Match(
            sql,
            "\\bLIMIT\\s+(?<bound>@[A-Za-z_][A-Za-z0-9_]*|[0-9]+)\\s*;?\\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Missing bounded LIMIT in {sourceTestPath}.");
        Assert.Equal(sqlLimitToken, match.Groups["bound"].Value);
        if (sqlLimitToken.StartsWith('@'))
        {
            var compactTestSource = Regex.Replace(
                testSource,
                "\\s+",
                string.Empty,
                RegexOptions.CultureInvariant);
            var proofExpression = (boundedProofExpression ?? limitRows.ToString(
                System.Globalization.CultureInfo.InvariantCulture))
                .Replace(" ", string.Empty, StringComparison.Ordinal);
            Assert.Contains(
                $"\"{sqlLimitToken}\",",
                compactTestSource,
                StringComparison.Ordinal);
            Assert.Contains(proofExpression, compactTestSource, StringComparison.Ordinal);
            return;
        }
        Assert.Equal(
            limitRows,
            int.Parse(sqlLimitToken, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void AssertKeysetContract(
        string sqlShape,
        IReadOnlyList<string> order,
        string predicateKind,
        string sourceTestPath)
    {
        var orderStart = sqlShape.IndexOf("order by ", StringComparison.Ordinal);
        var limitStart = sqlShape.LastIndexOf(" limit ", StringComparison.Ordinal);
        Assert.True(orderStart >= 0 && limitStart > orderStart, $"Missing bounded ORDER BY in {sourceTestPath}.");
        var orderClause = sqlShape[(orderStart + "order by ".Length)..limitStart];
        var scanFrom = 0;
        foreach (var element in order)
        {
            var parts = element.Split(':', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, parts.Length);
            var match = Regex.Match(
                orderClause[scanFrom..],
                $@"(?:[a-z0-9_]+\.)?\b{Regex.Escape(parts[0].ToLowerInvariant())}\b" +
                (string.Equals(parts[1], "asc", StringComparison.Ordinal)
                    ? "(?:\\s+asc)?"
                    : $@"\s+{Regex.Escape(parts[1].ToLowerInvariant())}"),
                RegexOptions.CultureInvariant);
            Assert.True(match.Success, $"Missing ordered keyset element {element} in {sourceTestPath}.");
            scanFrom += match.Index + match.Length;
        }

        switch (predicateKind)
        {
            case "strict-after-tuple":
                Assert.Contains("<", sqlShape, StringComparison.Ordinal);
                Assert.Matches(@"\bor\b", sqlShape);
                break;
            case "snapshot-upper-bound":
                Assert.Contains("<=", sqlShape, StringComparison.Ordinal);
                break;
            case "bounded-snapshot-range":
                Assert.Contains(">", sqlShape, StringComparison.Ordinal);
                Assert.Contains("<=", sqlShape, StringComparison.Ordinal);
                break;
            case "ordered-first-page":
                break;
            default:
                Assert.Fail($"Unknown keyset predicate kind {predicateKind} for {sourceTestPath}.");
                break;
        }
    }

    private static QueryPlanSource[] QueryCatalog() =>
    [
        new(
            QueryId: "comment.target-comments.cursor-page",
            ProofId: "comment.real-mariadb.cursor-explain",
            Service: "Comment",
            SourceTestPath: "Services/Comment/Comment.Tests/Comment.IntegrationTests/CommentCursorMariaDbIntegrationTests.cs",
            TestFullyQualifiedName:
                "Comment.UnitTests.CommentCursorMariaDbIntegrationTests.Keyset_pages_are_stable_without_count_or_offset_under_concurrent_insert",
            ExpectedIndex: "IX_CommentPost_Target_Visibility_Created_Id",
            ExplainTableName: "CommentPost",
            SqlSourcePath: null,
            SqlConstantName: null,
            PageSizeOptionsPath: null,
            IndexConfigurationPath:
                "Services/Comment/Comment.Infraestructure/Database/Ef/Configurations/Comments.cs",
            IndexMigrationPath:
                "Services/Comment/Comment.Infraestructure/Database/Ef/Migrations/20260717090922_HardenCommentSchema.cs",
            BoundedLimitRows: 51,
            BoundedSqlLimitToken: "51",
            BoundedProofExpression: null,
            BoundedLimitKind: "constant-page-size-plus-one",
            KeysetOrder: ["createdDate:desc", "id:desc"],
            KeysetPredicateKind: "strict-after-tuple"),
        new(
            QueryId: "feed.hybrid-pull.activity-page",
            ProofId: "feed.real-mariadb.pull-cursor-explain",
            Service: "Feed",
            SourceTestPath: "Tests/MariaDb.IntegrationTests/FeedDeliveryIntegrationTests.cs",
            TestFullyQualifiedName:
                "MariaDb.IntegrationTests.FeedDeliveryIntegrationTests.Hybrid_reader_caps_pull_publishers_at_64_and_paginates_a_stable_deduplicated_tuple",
            ExpectedIndex: "IX_FeedActivity_Pull_Cursor",
            ExplainTableName: "FeedActivity",
            SqlSourcePath: null,
            SqlConstantName: null,
            PageSizeOptionsPath: null,
            IndexConfigurationPath:
                "Services/Feed/Feed.Infraestructure/Database/Ef/Configurations/Feed.cs",
            IndexMigrationPath:
                "Services/Feed/Feed.Infraestructure/Database/Ef/Migrations/20260717240000_FeedDelivery.cs",
            BoundedLimitRows: 50,
            BoundedSqlLimitToken: "50",
            BoundedProofExpression: null,
            BoundedLimitKind: "constant-page-size",
            KeysetOrder: ["rankKey:desc", "publishedAtUtc:desc", "id:desc"],
            KeysetPredicateKind: "ordered-first-page"),
        new(
            QueryId: "following.follower-edges.cursor-page",
            ProofId: "following.real-mariadb.cursor-explain",
            Service: "Following",
            SourceTestPath:
                "Services/Following/Following.Tests/Following.IntegrationTests/FollowingCursorMariaDbIntegrationTests.cs",
            TestFullyQualifiedName:
                "Following.UnitTests.FollowingCursorMariaDbIntegrationTests.Real_keyset_query_is_stable_under_a_newer_concurrent_insert",
            ExpectedIndex: "IX_Follow_Follower_Cursor",
            ExplainTableName: "Follow",
            SqlSourcePath: null,
            SqlConstantName: null,
            PageSizeOptionsPath: null,
            IndexConfigurationPath:
                "Services/Following/Following.Infraestructure/Database/Ef/Configurations/Followings.cs",
            IndexMigrationPath:
                "Services/Following/Following.Infraestructure/Database/Ef/Migrations/20260717190000_HardenFollowingRelationships.cs",
            BoundedLimitRows: 51,
            BoundedSqlLimitToken: "51",
            BoundedProofExpression: null,
            BoundedLimitKind: "constant-page-size-plus-one",
            KeysetOrder: ["createdDate:desc", "id:desc"],
            KeysetPredicateKind: "strict-after-tuple"),
        new(
            QueryId: "interaction.authoritative-state.snapshot-revision",
            ProofId: "interaction.real-mariadb.authority-revision-explain",
            Service: "Interaction",
            SourceTestPath:
                "Services/Interaction/Interaction.Tests/Interaction.IntegrationTests/InteractionAuthoritativeExportMariaDbTests.cs",
            TestFullyQualifiedName:
                "Interaction.UnitTests.InteractionAuthoritativeExportMariaDbTests.Export_page_query_plan_uses_bounded_coordinate_and_latest_revision_indexes",
            ExpectedIndex: "IX_InteractionAuthorityRevision_Resource_Cursor",
            ExplainTableName: "InteractionAuthoritativeStateRevisions",
            SqlSourcePath:
                "Services/Interaction/Interaction.Infraestructure/Database/Ef/Repositories/InteractionRepository.cs",
            SqlConstantName: "AuthoritativePageSql",
            PageSizeOptionsPath:
                "Services/Interaction/Interaction.Application/Options/AuthoritativeExportOptions.cs",
            IndexConfigurationPath:
                "Services/Interaction/Interaction.Infraestructure/Database/Ef/Configurations/Interactions.cs",
            IndexMigrationPath:
                "Services/Interaction/Interaction.Infraestructure/Database/Ef/Migrations/20260718012150_AddImmutableInteractionAuthorityRevisions.cs",
            BoundedLimitRows: 501,
            BoundedSqlLimitToken: "@take",
            BoundedProofExpression: "AuthoritativeExportOptions.MaximumPageSize + 1",
            BoundedLimitKind: "parameterized-maximum-page-size-plus-one",
            KeysetOrder: ["revisionCursor:desc", "cursor:asc"],
            KeysetPredicateKind: "bounded-snapshot-range"),
        new(
            QueryId: "moderation.decision-state.snapshot-revision",
            ProofId: "moderation.real-mariadb.decision-revision-explain",
            Service: "Moderation",
            SourceTestPath:
                "Services/Moderation/Moderation.Tests/Moderation.IntegrationTests/ModerationDecisionExportMariaDbTests.cs",
            TestFullyQualifiedName:
                "Moderation.UnitTests.ModerationDecisionExportMariaDbTests.Export_page_query_plan_uses_bounded_coordinate_and_latest_revision_indexes",
            ExpectedIndex: "IX_ModerationDecisionRevision_Case_Cursor",
            ExplainTableName: "ModerationDecisionStateRevisions",
            SqlSourcePath:
                "Services/Moderation/Moderation.Infraestructure/Database/Ef/Repositories/ModerationRepository.cs",
            SqlConstantName: "DecisionAuthorityPageSql",
            PageSizeOptionsPath:
                "Services/Moderation/Moderation.Application/Options/ModerationDecisionExportOptions.cs",
            IndexConfigurationPath:
                "Services/Moderation/Moderation.Infraestructure/Database/Ef/Configurations/ModerationConfigurations.cs",
            IndexMigrationPath:
                "Services/Moderation/Moderation.Infraestructure/Database/Ef/Migrations/20260718012735_AddImmutableModerationDecisionRevisions.cs",
            BoundedLimitRows: 501,
            BoundedSqlLimitToken: "@take",
            BoundedProofExpression: "ModerationDecisionExportOptions.MaximumPageSize + 1",
            BoundedLimitKind: "parameterized-maximum-page-size-plus-one",
            KeysetOrder: ["revisionCursor:desc", "exportCursor:asc"],
            KeysetPredicateKind: "bounded-snapshot-range"),
    ];

    private static string Resolve(string repositoryRelativePath) =>
        Path.Combine(
            RepositoryRoot,
            repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string NormalizeLineEndings(string value) =>
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

internal sealed record QueryPlanSource(
    string QueryId,
    string ProofId,
    string Service,
    string SourceTestPath,
    string TestFullyQualifiedName,
    string ExpectedIndex,
    string ExplainTableName,
    string? SqlSourcePath,
    string? SqlConstantName,
    string? PageSizeOptionsPath,
    string IndexConfigurationPath,
    string IndexMigrationPath,
    int BoundedLimitRows,
    string BoundedSqlLimitToken,
    string? BoundedProofExpression,
    string BoundedLimitKind,
    IReadOnlyList<string> KeysetOrder,
    string KeysetPredicateKind);

internal sealed record BoundedQueryContract(int LimitRows, string LimitKind);

internal sealed record KeysetQueryContract(
    IReadOnlyList<string> Order,
    string PredicateKind);

internal sealed record QueryPlanBaselineItem(
    string QueryId,
    string ProofId,
    string Service,
    string SourceTestPath,
    string TestFullyQualifiedName,
    string ExpectedIndex,
    BoundedQueryContract Bounded,
    KeysetQueryContract Keyset,
    string NormalizedSqlShapeSha256);

internal sealed record QueryPlanBaseline(
    int FormatVersion,
    string RegenerationCommand,
    string Scope,
    string FleetQueryShapeSha256,
    IReadOnlyList<QueryPlanBaselineItem> Queries);
