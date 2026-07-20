using PerformanceTesting;

namespace PerformanceTesting.UnitTests;

public sealed class PerformanceValidationTests
{
    [Fact]
    public void Weighted_selection_is_deterministic_and_exact_over_complete_cycles()
    {
        var operations = new[]
        {
            Operation("read", 2),
            Operation("write", 1, HttpMethod.Post),
        };

        var selected = Enumerable.Range(0, 300)
            .Select(sequence => WeightedOperationSelector.Select(operations, sequence).Name)
            .ToArray();

        Assert.Equal("read", selected[0]);
        Assert.Equal("read", selected[1]);
        Assert.Equal("write", selected[2]);
        Assert.Equal(200, selected.Count(name => name == "read"));
        Assert.Equal(100, selected.Count(name => name == "write"));
    }

    [Theory]
    [InlineData("https://example.test/path")]
    [InlineData("//example.test/path")]
    [InlineData("/path#fragment")]
    [InlineData("/path\\child")]
    [InlineData("/path\r\nInjected: value")]
    public void Operation_paths_are_application_relative_and_bounded(string path)
    {
        var operation = Operation("invalid-path", 1) with { RequestPath = path };

        Assert.Throws<PerformanceConfigurationException>(() =>
            PerformanceProfileValidator.ValidateOperation(operation));
    }

    [Fact]
    public void Operation_bodies_are_bounded_by_utf8_bytes()
    {
        var operation = Operation("oversized", 1, HttpMethod.Post) with
        {
            RequestBody = new string('\u00e9', PerformanceLimits.MaximumBodyBytes),
        };

        var exception = Assert.Throws<PerformanceConfigurationException>(() =>
            PerformanceProfileValidator.ValidateOperation(operation));
        Assert.Contains("UTF-8 bytes", exception.Message);
    }

    [Fact]
    public void Profile_mix_rejects_duplicate_operations_and_invalid_weights()
    {
        var document = new PerformanceProfileDocument
        {
            Operations = new()
            {
                ["one"] = new() { Path = "/one", Method = "GET" },
                ["two"] = new() { Path = "/two", Method = "GET" },
            },
            Profiles = new()
            {
                ["smoke"] = new()
                {
                    ArrivalModel = "paced-fixed-rate",
                    Mix =
                    [
                        new() { Operation = "one", Weight = 1 },
                        new() { Operation = "ONE", Weight = PerformanceLimits.MaximumOperationWeight + 1 },
                    ],
                },
            },
        };

        Assert.Throws<PerformanceConfigurationException>(() =>
            PerformanceProfileValidator.ValidateDocument(document));
    }

    private static PerformanceOperation Operation(
        string name,
        int weight,
        HttpMethod? method = null) => new(
            name,
            weight,
            "/api/test",
            method ?? HttpMethod.Get,
            method == HttpMethod.Post ? "{}" : null,
            "application/json",
            RequiresAuthentication: false);
}
