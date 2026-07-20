using System.Text.Json;
using System.Text.Json.Serialization;
using CommomStartupExtensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace CommomStartupExtensions.UnitTests;

public sealed class JsonSerializationContractTests
{
    [Fact]
    public void Controllers_and_minimal_apis_share_the_strict_system_text_json_contract()
    {
        var (mvc, http) = ResolveSerializerOptions();

        AssertStrictContract(mvc);
        AssertStrictContract(http);

        var value = new SerializationProbe
        {
            At = new DateTime(2026, 7, 18, 12, 34, 56, DateTimeKind.Utc).AddTicks(1_234_567),
            Offset = new DateTimeOffset(2026, 7, 18, 14, 34, 56, TimeSpan.FromHours(2))
                .AddTicks(7_654_321),
            Count = 7,
            State = ProbeState.Ready,
        };

        var mvcJson = JsonSerializer.Serialize(value, mvc);
        var httpJson = JsonSerializer.Serialize(value, http);

        Assert.Equal(mvcJson, httpJson);
        Assert.Contains("\"state\":\"Ready\"", mvcJson, StringComparison.Ordinal);
        Assert.Contains("2026-07-18T12:34:56.1234567Z", mvcJson, StringComparison.Ordinal);
        Assert.Contains("2026-07-18T12:34:56.7654321Z", mvcJson, StringComparison.Ordinal);

        foreach (var options in new[] { mvc, http })
        {
            var deserialized = JsonSerializer.Deserialize<SerializationProbe>(
                """{"at":"2026-07-18T14:34:56+02:00","offset":"2026-07-18T14:34:56+02:00","count":7,"state":"Ready"}""",
                options);

            Assert.NotNull(deserialized);
            Assert.Equal(DateTimeKind.Utc, deserialized.At.Kind);
            Assert.Equal(new DateTime(2026, 7, 18, 12, 34, 56, DateTimeKind.Utc), deserialized.At);
            Assert.Equal(TimeSpan.FromHours(2), deserialized.Offset.Offset);
            Assert.Equal(
                new DateTimeOffset(2026, 7, 18, 14, 34, 56, TimeSpan.FromHours(2)),
                deserialized.Offset);
        }
    }

    [Theory]
    [InlineData("""{"at":""}""")]
    [InlineData("""{"at":42}""")]
    [InlineData("""{"offset":""}""")]
    [InlineData("""{"offset":false}""")]
    [InlineData("""{"count":""}""")]
    [InlineData("""{"state":1}""")]
    [InlineData("""{"state":"not-a-state"}""")]
    public void Both_contracts_reject_blank_or_malformed_typed_json(string json)
    {
        var (mvc, http) = ResolveSerializerOptions();

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SerializationProbe>(json, mvc));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SerializationProbe>(json, http));
    }

    [Fact]
    public void Both_contracts_fail_closed_on_reference_cycles()
    {
        var (mvc, http) = ResolveSerializerOptions();
        var value = new CycleProbe();
        value.Next = value;

        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(value, mvc));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(value, http));
    }

    [Fact]
    public void ConfigureControllers_registers_only_system_text_json_formatters()
    {
        var services = new ServiceCollection();
        services.ConfigureControllers();

        Assert.DoesNotContain(
            services,
            descriptor => Describe(descriptor).Contains("Newtonsoft", StringComparison.OrdinalIgnoreCase));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MvcOptions>>().Value;
        var formatterTypes = options.InputFormatters
            .Cast<object>()
            .Concat(options.OutputFormatters)
            .Select(formatter => formatter.GetType())
            .ToArray();

        Assert.Contains(formatterTypes, type => type.Name == "SystemTextJsonInputFormatter");
        Assert.Contains(formatterTypes, type => type.Name == "SystemTextJsonOutputFormatter");
        Assert.DoesNotContain(
            formatterTypes,
            type => (type.FullName ?? type.Name).Contains(
                "Newtonsoft",
                StringComparison.OrdinalIgnoreCase));
    }

    private static (JsonSerializerOptions Mvc, JsonSerializerOptions Http) ResolveSerializerOptions()
    {
        var services = new ServiceCollection();
        services.ConfigureControllers();
        using var provider = services.BuildServiceProvider();

        return (
            provider.GetRequiredService<IOptions<MvcJsonOptions>>().Value.JsonSerializerOptions,
            provider.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions);
    }

    private static void AssertStrictContract(JsonSerializerOptions options)
    {
        Assert.Contains(options.Converters, converter => converter is JsonStringEnumConverter);
        Assert.Contains(options.Converters, converter => converter is UtcIso8601DateTimeConverter);
        Assert.Contains(options.Converters, converter => converter is UtcIso8601DateTimeOffsetConverter);
        Assert.Null(options.ReferenceHandler);
    }

    private static string Describe(ServiceDescriptor descriptor) => string.Join(
        '|',
        descriptor.ServiceType.AssemblyQualifiedName,
        descriptor.ImplementationType?.AssemblyQualifiedName,
        descriptor.ImplementationInstance?.GetType().AssemblyQualifiedName,
        descriptor.ImplementationFactory?.GetType().AssemblyQualifiedName);

    private sealed class SerializationProbe
    {
        public DateTime At { get; set; }
        public DateTimeOffset Offset { get; set; }
        public int Count { get; set; }
        public ProbeState State { get; set; }
    }

    private sealed class CycleProbe
    {
        public CycleProbe? Next { get; set; }
    }

    private enum ProbeState
    {
        Ready,
    }
}
