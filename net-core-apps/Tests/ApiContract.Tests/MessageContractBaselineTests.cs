using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MessageBrokerContracts.Feed;
using Xunit;

namespace ApiContract.Tests;

public sealed class MessageContractBaselineTests
{
    [Fact]
    public void Sanitized_message_contract_baseline_is_deterministic_and_current()
    {
        var manifest = BuildManifest();
        var baselinePath = Path.Combine(
            FindRepositoryRoot(),
            "Tests",
            "ApiContract.Tests",
            "Baselines",
            "message-contract-manifest.json");

        if (string.Equals(
                Environment.GetEnvironmentVariable("UPDATE_MESSAGE_CONTRACT_BASELINE"),
                "1",
                StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            File.WriteAllText(baselinePath, manifest, new UTF8Encoding(false));
            return;
        }

        Assert.True(File.Exists(baselinePath), $"Missing message baseline: {baselinePath}");
        Assert.Equal(
            File.ReadAllText(baselinePath).Replace("\r\n", "\n", StringComparison.Ordinal),
            manifest);
    }

    private static string BuildManifest()
    {
        var contracts = typeof(FeedActivityChanged).Assembly
            .GetExportedTypes()
            .Where(type => type.IsClass && !(type.IsAbstract && type.IsSealed))
            .Select(Describe)
            .OrderBy(item => item.Contract, StringComparer.Ordinal)
            .ToArray();

        var combinedShape = string.Join(
            "\n---\n",
            contracts.Select(contract =>
                $"{contract.Contract}\n" +
                string.Join("\n", contract.Fields.Select(field =>
                    $"{field.WireName}:{field.ClrType}:required={field.Required}:omitWhenNull={field.OmitWhenNull}"))));
        var document = new MessageContractManifest(
            FormatVersion: 2,
            SourceEvidenceCommit: "f969b7363bbac5787625ed0875264abe6a2c415c",
            RegenerationCommand:
                "pwsh -File Tests/ApiContract.Tests/Baselines/regenerate-message-contract-baseline.ps1",
            ContractCount: contracts.Length,
            ContractShapeSha256: Sha256(combinedShape),
            Contracts: contracts);

        return System.Text.Json.JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static MessageContractShape Describe(Type type)
    {
        var fields = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetMethod is not null)
            .Where(property => property.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>()?.Condition
                is not JsonIgnoreCondition.Always)
            .Select(property =>
            {
                var systemText = property.GetCustomAttribute<JsonPropertyNameAttribute>();
                var wireName = systemText?.Name
                    ?? property.Name;
                var nullable = Nullable.GetUnderlyingType(property.PropertyType) is not null
                    || (!property.PropertyType.IsValueType &&
                        property.CustomAttributes.Any(attribute =>
                            attribute.AttributeType.FullName ==
                            "System.Runtime.CompilerServices.NullableAttribute"));
                var omitWhenNull = property.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>()?.Condition ==
                    JsonIgnoreCondition.WhenWritingNull;
                return new MessageContractField(
                    wireName,
                    FriendlyType(property.PropertyType),
                    property.GetCustomAttribute<RequiredMemberAttribute>() is not null || !nullable,
                    omitWhenNull);
            })
            .OrderBy(field => field.WireName, StringComparer.Ordinal)
            .ToArray();
        return new MessageContractShape(
            type.FullName ?? type.Name,
            fields);
    }

    private static string FriendlyType(Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
            return FriendlyType(nullable) + "?";
        if (type.IsArray)
            return FriendlyType(type.GetElementType()!) + "[]";
        if (!type.IsGenericType)
            return type.FullName ?? type.Name;
        var name = type.GetGenericTypeDefinition().FullName
            ?? type.GetGenericTypeDefinition().Name;
        name = name[..name.IndexOf('`')];
        return $"{name}<{string.Join(',', type.GetGenericArguments().Select(FriendlyType))}>";
    }

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

internal sealed record MessageContractField(
    string WireName,
    string ClrType,
    bool Required,
    bool OmitWhenNull);

internal sealed record MessageContractShape(
    string Contract,
    IReadOnlyList<MessageContractField> Fields);

internal sealed record MessageContractManifest(
    int FormatVersion,
    string SourceEvidenceCommit,
    string RegenerationCommand,
    int ContractCount,
    string ContractShapeSha256,
    IReadOnlyList<MessageContractShape> Contracts);
