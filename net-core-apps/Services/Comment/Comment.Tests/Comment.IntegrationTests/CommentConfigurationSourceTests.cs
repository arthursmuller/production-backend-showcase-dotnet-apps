using System.Text.Json;
using Xunit;
using Comment.Application.Features.Comments.Queries;
using Comment.Application.Features.Privacy;
using Comment.Application.Features.Visibility;
using Comment.Infra.Providers;
using Comment.Infra.Providers.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Comment.UnitTests;

public sealed class CommentConfigurationSourceTests
{
    [Fact]
    public void Comment_media_rejects_a_publicly_readable_storage_backend()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Runtime:Role"] = "Api",
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["Storage:Minio:enabled"] = "true",
                ["Storage:Minio:endpoint"] = "127.0.0.1:9000",
                ["Storage:Minio:accessKey"] = "test",
                ["Storage:Minio:secretKey"] = "test-secret",
                ["Storage:Minio:bucket"] = "comment-tests",
                ["Storage:Minio:useSSL"] = "false",
                ["Storage:Minio:allowPublicRead"] = "true",
            })
            .Build();
        var services = new ServiceCollection();

        var error = Assert.Throws<InvalidOperationException>(() =>
            services.ConfigureStorage(configuration));

        Assert.Contains("publicly readable", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_cursor_and_job_media_secrets_require_local_external_overrides()
    {
        var repository = FindRepositoryRoot();
        using var production = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repository,
            "net-core-apps",
            "Services",
            "Comment",
            "Comment.Api",
            "appsettings.json")));
        using var development = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repository,
            "net-core-apps",
            "Services",
            "Comment",
            "Comment.Api",
            "appsettings.Development.json")));

        Assert.Equal(
            "{{secret:CommentCursorSigningKey}}",
            production.RootElement.GetProperty("CommentCursor")
                .GetProperty("SigningKey").GetString());
        Assert.True(string.IsNullOrEmpty(development.RootElement.GetProperty("CommentCursor")
            .GetProperty("SigningKey").GetString()));

        var productionPath = Path.Combine(repository, "net-core-apps", "Services", "Comment",
            "Comment.Api", "appsettings.json");
        var developmentPath = Path.Combine(repository, "net-core-apps", "Services", "Comment",
            "Comment.Api", "appsettings.Development.json");
        var unresolved = new ConfigurationBuilder()
            .AddJsonFile(productionPath, optional: false)
            .AddJsonFile(developmentPath, optional: false)
            .Build()
            .GetSection(CommentCursorOptions.SectionName)
            .Get<CommentCursorOptions>();
        Assert.NotNull(unresolved);
        Assert.False(CommentCursorOptions.IsValid(unresolved));

        const string prefix = "COMMENT_CREDENTIAL_TEST_";
        const string variable = prefix + "CommentCursor__SigningKey";
        Environment.SetEnvironmentVariable(variable, Convert.ToBase64String(new byte[32]));
        try
        {
            var resolved = new ConfigurationBuilder()
                .AddJsonFile(productionPath, optional: false)
                .AddJsonFile(developmentPath, optional: false)
                .AddEnvironmentVariables(prefix)
                .Build()
                .GetSection(CommentCursorOptions.SectionName)
                .Get<CommentCursorOptions>();
            Assert.NotNull(resolved);
            Assert.True(CommentCursorOptions.IsValid(resolved));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
        var storageRoles = production.RootElement
            .GetProperty("SecretProvider")
            .GetProperty("References")
            .GetProperty("StorageOciConnection")
            .GetProperty("Roles")
            .EnumerateArray()
            .Select(role => role.GetString())
            .ToArray();
        Assert.Contains("Api", storageRoles);
        Assert.Contains("JobWorker", storageRoles);
        Assert.Contains("Combined", storageRoles);
        Assert.False(development.RootElement.GetProperty("Storage")
            .GetProperty("Minio")
            .GetProperty("allowPublicRead")
            .GetBoolean());
    }

    [Fact]
    public void Relationship_snapshot_credentials_and_egress_are_job_only_and_activation_is_explicit()
    {
        var repository = FindRepositoryRoot();
        using var production = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository,
            "net-core-apps", "Services", "Comment", "Comment.Api", "appsettings.json")));
        using var development = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository,
            "net-core-apps", "Services", "Comment", "Comment.Api", "appsettings.Development.json")));
        var root = production.RootElement;
        Assert.False(root.GetProperty("CommentAudienceProjection")
            .GetProperty("EnableReadEnforcement").GetBoolean());
        Assert.Equal("{{secret:FollowingSnapshotClientSecret}}", root.GetProperty("Following")
            .GetProperty("Snapshot").GetProperty("ClientSecret").GetString());
        Assert.True(string.IsNullOrEmpty(development.RootElement.GetProperty("Following")
            .GetProperty("Snapshot").GetProperty("ClientSecret").GetString()));

        var references = root.GetProperty("SecretProvider").GetProperty("References");
        var snapshotRoles = references.GetProperty("FollowingSnapshotClientSecret")
            .GetProperty("Roles").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.Equal(new[] { "JobWorker", "Combined" }, snapshotRoles);
        var identityRoles = references.GetProperty("IdentityClientSecret")
            .GetProperty("Roles").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.DoesNotContain("JobWorker", identityRoles);
        Assert.DoesNotContain("ConsumerWorker", snapshotRoles);
        Assert.DoesNotContain("Migration", snapshotRoles);
        Assert.DoesNotContain("Api", snapshotRoles);

        var values = File.ReadAllText(Path.Combine(repository, "net-core-apps", "DevOps",
            "environments", "production", "comment.values.example.yaml"));
        Assert.Contains("Following__ServiceUrl:", values, StringComparison.Ordinal);
        Assert.Contains("kubernetes.io/metadata.name: following", values, StringComparison.Ordinal);
        var apiSection = values.Split("  consumer:", StringSplitOptions.None)[0];
        Assert.DoesNotContain("Following__ServiceUrl:", apiSection, StringComparison.Ordinal);
        Assert.Contains(
            "CommentAudienceProjection__EnableReadEnforcement: \"true\"",
            apiSection,
            StringComparison.Ordinal);

        var followingValues = File.ReadAllText(Path.Combine(repository, "net-core-apps", "DevOps",
            "environments", "production", "following.values.example.yaml"));
        Assert.Contains("apiIngress:", followingValues, StringComparison.Ordinal);
        Assert.Contains("kubernetes.io/metadata.name: comment", followingValues,
            StringComparison.Ordinal);
        Assert.Contains("app.kubernetes.io/name: comment", followingValues,
            StringComparison.Ordinal);
        Assert.Contains("inspacy.io/runtime-role: job", followingValues,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Jobs_are_concrete_and_have_no_jobs_extensions_indirection()
    {
        var jobs = Path.Combine(FindRepositoryRoot(), "net-core-apps", "Services", "Comment",
            "Comment.Api", "AppInterfaces", "Jobs");
        Assert.NotEmpty(Directory.EnumerateFiles(jobs, "*Job.cs"));
        Assert.Empty(Directory.EnumerateFiles(jobs, "*JobsExtensions*.cs"));
    }

    [Fact]
    public void User_erasure_work_is_hard_bounded_and_configured_only_on_the_job_role()
    {
        var repository = FindRepositoryRoot();
        using var production = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository,
            "net-core-apps", "Services", "Comment", "Comment.Api", "appsettings.json")));
        var options = JsonSerializer.Deserialize<CommentUserErasureOptions>(
            production.RootElement.GetProperty(CommentUserErasureOptions.SectionName).GetRawText());

        Assert.NotNull(options);
        Assert.True(CommentUserErasureOptions.IsValid(options));
        Assert.True(options.LeaseDuration >= options.CycleTimeBudget + options.CycleTimeBudget);
        Assert.InRange(options.BatchSize, 1, 100);
        Assert.InRange(options.MaximumRowsPerCycle, options.BatchSize, 2_000);

        var values = File.ReadAllText(Path.Combine(repository, "net-core-apps", "DevOps",
            "environments", "production", "comment.values.example.yaml"));
        var apiAndConsumer = values.Split("  job:", StringSplitOptions.None)[0];
        var job = values.Split("  job:", StringSplitOptions.None)[1]
            .Split("migration:", StringSplitOptions.None)[0];
        Assert.DoesNotContain("CommentUserErasure__", apiAndConsumer, StringComparison.Ordinal);
        Assert.Contains("CommentUserErasure__BatchSize:", job, StringComparison.Ordinal);
        Assert.Contains("CommentUserErasure__CycleTimeBudgetMilliseconds:", job,
            StringComparison.Ordinal);
        Assert.Contains("CommentUserErasure__LeaseSeconds:", job, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Api", false)]
    [InlineData("ConsumerWorker", false)]
    [InlineData("Migration", false)]
    [InlineData("JobWorker", true)]
    [InlineData("Combined", true)]
    public void Following_snapshot_source_is_registered_only_for_job_owning_roles(
        string role, bool expected)
    {
        var settings = Path.Combine(FindRepositoryRoot(), "net-core-apps", "Services",
            "Comment", "Comment.Api", "appsettings.json");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(settings, optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Runtime:Role"] = role,
                ["Storage:Minio:enabled"] = "true",
                ["Storage:Minio:endpoint"] = "127.0.0.1:9000",
                ["Storage:Minio:accessKey"] = "test",
                ["Storage:Minio:secretKey"] = "test-secret",
                ["Storage:Minio:bucket"] = "comment-tests",
                ["Storage:Minio:useSSL"] = "true",
                ["Storage:Minio:publicUrl"] = "https://127.0.0.1:9000",
            })
            .Build();
        var services = new ServiceCollection();

        services.ConfigureProviders(configuration);

        Assert.Equal(expected, services.Any(item =>
            item.ServiceType == typeof(ICommentRelationshipSnapshotSource)));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                current.FullName,
                "net-core-apps",
                "Services",
                "Comment",
                "Comment.Api",
                "appsettings.json")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("The repository root could not be located.");
    }
}
