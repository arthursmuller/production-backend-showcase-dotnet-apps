using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace StorageProvider.Providers.Oci;

public static class OciStorageProviderExtensions
{
    private const int MaxSerializedOptionsLength = 64 * 1024;

    public static IServiceCollection ConfigureOciStorageProvider(
        this IServiceCollection services,
        OciObjectStorageOptions opts)
    {
        services.AddSingleton(opts);
        services.AddScoped<IStorageProvider, OciObjectStorageProvider>();
        return services;
    }

    public static OciObjectStorageOptions? BuildOptions(IConfiguration? configuration = null)
    {
        var json = configuration?["Storage:OciConnection"];
        if (string.IsNullOrWhiteSpace(json) || IsUnresolvedSecret(json))
            return null;
        if (json.Length > MaxSerializedOptionsLength)
            throw new InvalidOperationException("Storage:OciConnection exceeds the maximum supported size.");

        try
        {
            var options = JsonSerializer.Deserialize<OciObjectStorageOptions>(json)
                ?? throw new InvalidOperationException("Storage:OciConnection could not be deserialized.");
            Validate(options, configuration!);
            return options;
        }
        catch (JsonException)
        {
            // Do not include the secret-bearing JSON or parser details in the startup error.
            throw new InvalidOperationException("Storage:OciConnection contains invalid JSON.");
        }
    }

    private static bool IsUnresolvedSecret(string value) =>
        value.Contains("{{secret", StringComparison.Ordinal);

    private static void Validate(
        OciObjectStorageOptions options,
        IConfiguration configuration)
    {
        if (!options.Enabled)
            return;

        options.AuthMode = Required(options.AuthMode, "authMode");
        options.Region = Required(options.Region, "region");
        options.Namespace = Required(options.Namespace, "namespace");
        options.BucketName = Required(options.BucketName, "bucketName");
        options.AccessMode = Required(options.AccessMode, "accessMode");
        if (!options.AccessMode.Equals(OciObjectStorageOptions.PublicAccessMode,
                StringComparison.OrdinalIgnoreCase)
            && !options.AccessMode.Equals(OciObjectStorageOptions.PrivateAccessMode,
                StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidSetting("accessMode must be Public or Private");
        }
        if (!options.UsesPrivateAccess)
            options.PublicBaseUrl = Required(options.PublicBaseUrl, "publicBaseUrl");
        else if (!string.IsNullOrWhiteSpace(options.PublicBaseUrl))
            throw InvalidSetting("publicBaseUrl must be omitted for Private accessMode");
        if (!IsSafeIdentifier(options.Region, 64)
            || !IsSafeIdentifier(options.Namespace, 256)
            || !IsSafeIdentifier(options.BucketName, 256))
        {
            throw InvalidSetting("region, namespace, or bucketName contains unsafe characters");
        }
        if (options.MaximumUploadBytes is < 1 or > 100L * 1024 * 1024)
            throw InvalidSetting("maximumUploadBytes must be between 1 and 104857600");
        if (!string.IsNullOrWhiteSpace(options.PrivateKeyPem))
            throw InvalidSetting("privateKeyPem is prohibited; mount a read-only key file for ApiKey mode");

        var environment = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? "Production";
        var isDevelopment = environment.Equals("Development", StringComparison.OrdinalIgnoreCase);
        if (!options.UsesPrivateAccess
            && (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var publicUrl)
                || (!isDevelopment && !publicUrl.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                || !string.IsNullOrEmpty(publicUrl.UserInfo)
                || !string.IsNullOrEmpty(publicUrl.Query)
                || !string.IsNullOrEmpty(publicUrl.Fragment)))
        {
            throw InvalidSetting("publicBaseUrl must be a clean absolute HTTPS CDN URL outside Development");
        }

        switch (options.AuthMode.ToLowerInvariant())
        {
            case "resourceprincipal":
            case "instanceprincipal":
                RejectApiKeyMaterial(options);
                break;
            case "apikey":
                options.TenancyOcid = Required(options.TenancyOcid, "tenancyOcid");
                options.UserOcid = Required(options.UserOcid, "userOcid");
                options.Fingerprint = Required(options.Fingerprint, "fingerprint");
                options.PrivateKeyPath = Required(options.PrivateKeyPath, "privateKeyPath");
                if (!Path.IsPathRooted(options.PrivateKeyPath))
                    throw InvalidSetting("privateKeyPath must be an absolute mounted path");
                break;
            default:
                throw InvalidSetting("authMode must be ResourcePrincipal, InstancePrincipal, or ApiKey");
        }
    }

    private static void RejectApiKeyMaterial(OciObjectStorageOptions options)
    {
        if (new[]
            {
                options.TenancyOcid,
                options.UserOcid,
                options.Fingerprint,
                options.PrivateKeyPath,
                options.PrivateKeyPassphrase,
            }.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            throw InvalidSetting("API-key fields cannot be supplied with workload-principal authentication");
        }
    }

    private static string Required(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value) || IsUnresolvedSecret(value))
            throw InvalidSetting($"{key} is required when OCI storage is enabled");
        return value.Trim();
    }

    private static bool IsSafeIdentifier(string value, int maximumLength) =>
        value.Length <= maximumLength
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.');

    private static InvalidOperationException InvalidSetting(string reason) =>
        new($"Storage:OciConnection {reason}.");
}
