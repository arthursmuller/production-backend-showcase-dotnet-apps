using System.Text.Json;
using PerformanceTesting;

namespace PerformanceTesting.UnitTests;

public sealed class RepositoryProfileTests
{
    [Fact]
    public void Every_domain_has_distinct_valid_load_stress_and_soak_profiles()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "RepositoryProfiles");
        var files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories);
        Assert.Equal(15, files.Length);

        foreach (var domainDirectory in Directory.GetDirectories(root))
        {
            var domain = Path.GetFileName(domainDirectory);
            var domainFiles = Directory.GetFiles(domainDirectory, "*.json");
            Assert.Equal(3, domainFiles.Length);

            var arrivalModels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in domainFiles)
            {
                var workload = Path.GetFileNameWithoutExtension(file);
                var expectedArrivalModel = workload switch
                {
                    "load" => "paced-fixed-rate",
                    "stress" => "linear-concurrency-ramp-with-recovery",
                    "soak" => "periodic-burst",
                    _ => throw new Xunit.Sdk.XunitException($"Unexpected workload file '{file}'."),
                };

                var document = PerformanceProfileValidator.ReadDocument(file);
                Assert.True(document.Profiles.TryGetValue("smoke", out var smoke));
                Assert.True(document.Profiles.TryGetValue("baseline", out var baseline));
                Assert.True(document.Profiles.TryGetValue("representative", out var representative));
                Assert.True(smoke!.Enabled);
                Assert.False(baseline!.Enabled);
                Assert.False(representative!.Enabled);
                Assert.Equal(expectedArrivalModel, smoke.ArrivalModel);
                Assert.Equal(expectedArrivalModel, baseline.ArrivalModel);
                Assert.Equal(expectedArrivalModel, representative.ArrivalModel);
                Assert.True(arrivalModels.Add(expectedArrivalModel));

                var smokeOperations = PerformanceProfileValidator.ResolveOperations(document, smoke);
                Assert.True(smokeOperations.Count >= 2, $"{domain}/{workload} smoke mix is not multi-operation.");
                Assert.All(smokeOperations, operation =>
                {
                    Assert.False(operation.IsMutation);
                    Assert.False(operation.RequiresAuthentication);
                });

                Assert.Null(baseline.DurationSeconds);
                Assert.Null(baseline.Concurrency);
                Assert.Null(baseline.TargetRps);
                Assert.Null(baseline.RequestTimeoutSeconds);
                Assert.Null(representative.DurationSeconds);
                Assert.Null(representative.Concurrency);
                Assert.Null(representative.TargetRps);
                Assert.Null(representative.RequestTimeoutSeconds);

                var raw = File.ReadAllText(file);
                Assert.DoesNotContain("accessToken", raw, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("authorization", raw, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("bearer ", raw, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Equal(3, arrivalModels.Count);
        }
    }

    [Fact]
    public void Profile_and_result_schemas_are_valid_json_and_result_schema_excludes_secrets()
    {
        var profileSchema = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "performance-profiles.schema.json"));
        var resultSchema = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "performance-result.schema.json"));

        using var _ = JsonDocument.Parse(profileSchema);
        using var __ = JsonDocument.Parse(resultSchema);
        Assert.DoesNotContain("requestBody", resultSchema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requestPath", resultSchema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", resultSchema, StringComparison.OrdinalIgnoreCase);
    }
}
