using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecretProvider.Configuration;
using SecretProvider.Providers.Api;
using Xunit;

namespace SecretProvider.UnitTests;

public sealed class SecretConfigurationProviderTests
{
    [Fact]
    public void Placeholder_supports_embedded_aliases_and_deduplicated_references()
    {
        var options = Options();
        var value = "Server=db;Password={{secret:DatabasePassword}};" +
            "Again={{secret:DatabasePassword}}";

        var references = SecretPlaceholder.GetReferences(value, options);
        var resolved = SecretPlaceholder.Resolve(
            value,
            options,
            new Dictionary<SecretReference, string>
            {
                [new("database", "password")] = "resolved-db",
            });

        Assert.Single(references);
        Assert.Equal(
            "Server=db;Password=resolved-db;Again=resolved-db",
            resolved);
    }

    [Theory]
    [InlineData("{{secret}}")]
    [InlineData("{{secret:}}")]
    [InlineData("{{secret:missing-alias}}")]
    [InlineData("prefix {{secret?://missing-key}} suffix")]
    public void Placeholder_rejects_malformed_or_unmapped_reserved_values(string value)
    {
        Assert.Throws<InvalidOperationException>(() =>
            SecretPlaceholder.Validate(value, Options()));
    }

    [Fact]
    public void Provider_coalesces_calls_and_TryGet_is_snapshot_only()
    {
        var resolver = new RecordingProvider(reference => $"value-for-{reference.Key}");
        var source = new SecretApiConfigurationSource(
            Options(),
            new Dictionary<string, string>
            {
                ["One"] = "{{secret:DatabasePassword}}",
                ["Embedded"] = "before-{{secret:DatabasePassword}}-after",
            },
            _ => resolver);

        var provider = source.Build(new ConfigurationBuilder());
        provider.Load();

        Assert.True(provider.TryGet("One", out var one));
        Assert.Equal("value-for-password", one);
        Assert.True(provider.TryGet("Embedded", out var embedded));
        Assert.Equal("before-value-for-password-after", embedded);
        Assert.False(provider.TryGet("Unknown", out _));

        for (var index = 0; index < 100; index++)
            Assert.True(provider.TryGet("One", out _));

        Assert.Single(resolver.Requests);
    }

    [Fact]
    public void Running_snapshot_ignores_lower_source_reload_and_new_process_gets_rotated_value()
    {
        var currentValue = "snapshot-one";
        var firstResolver = new RecordingProvider(_ => currentValue);
        var reloadableSource = new ReloadableConfigurationSource(new Dictionary<string, string?>
        {
            ["Connection"] = "{{secret:DatabasePassword}}",
        });
        var firstConfiguration = new ConfigurationBuilder()
            .Add(reloadableSource)
            .Add(new SecretApiConfigurationSource(
                Options(),
                new Dictionary<string, string>
                {
                    ["Connection"] = "{{secret:DatabasePassword}}",
                },
                _ => firstResolver))
            .Build();

        Assert.Equal("snapshot-one", firstConfiguration["Connection"]);
        Assert.Single(firstResolver.Requests);

        currentValue = "snapshot-two";
        reloadableSource.Provider!.SetAndReload("Connection", "lower-source-changed");

        Assert.Equal("snapshot-one", firstConfiguration["Connection"]);
        Assert.Single(firstResolver.Requests);

        var restartedResolver = new RecordingProvider(_ => currentValue);
        var restartedConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Connection"] = "{{secret:DatabasePassword}}",
            })
            .Add(new SecretApiConfigurationSource(
                Options(),
                new Dictionary<string, string>
                {
                    ["Connection"] = "{{secret:DatabasePassword}}",
                },
                _ => restartedResolver))
            .Build();

        Assert.Equal("snapshot-two", restartedConfiguration["Connection"]);
        Assert.Single(restartedResolver.Requests);
    }

    [Fact]
    public void A_later_plain_override_is_not_sent_to_the_secret_backend()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["Connection"] = "{{secret:DatabasePassword}}",
            ["SecretProvider:References:DatabasePassword:Namespace"] = "database",
            ["SecretProvider:References:DatabasePassword:Key"] = "password",
        });
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Connection"] = "operator-override",
        });

        configuration.AddSecretApiProvider();

        Assert.Equal("operator-override", configuration["Connection"]);
    }

    [Fact]
    public void Migration_role_fetches_only_its_explicitly_authorized_secret()
    {
        var options = new SecretApiOptions
        {
            References = new Dictionary<string, SecretAliasOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["DatabasePassword"] = new()
                {
                    Namespace = "database",
                    Key = "password",
                    Roles = ["Api", "ConsumerWorker", "JobWorker", "Migration"],
                },
                ["IdentityClientSecret"] = new()
                {
                    Namespace = "identity",
                    Key = "client-secret",
                    Roles = ["Api"],
                },
            },
        };
        var entries = new Dictionary<string, string?>
        {
            ["Database:Password"] = "{{secret:DatabasePassword}}",
            ["Identity:ClientSecret"] = "{{secret:IdentityClientSecret}}",
        };
        var selected = SecretProviderExtensions
            .SelectRolePlaceholders(entries, options, "Migration")
            .ToDictionary(entry => entry.Key, entry => entry.Value!);
        var resolver = new RecordingProvider(reference => $"resolved-{reference.Key}");
        var provider = new SecretApiConfigurationSource(options, selected, _ => resolver)
            .Build(new ConfigurationBuilder());

        provider.Load();

        Assert.True(provider.TryGet("Database:Password", out _));
        Assert.False(provider.TryGet("Identity:ClientSecret", out _));
        var request = Assert.Single(resolver.Requests);
        Assert.Equal(new SecretReference("database", "password"), request);
    }

    [Fact]
    public void Role_scoped_resolution_rejects_an_alias_without_a_role_allow_list()
    {
        var entries = new Dictionary<string, string?>
        {
            ["Database:Password"] = "{{secret:DatabasePassword}}",
        };

        Assert.Throws<InvalidOperationException>(() => SecretProviderExtensions
            .SelectRolePlaceholders(entries, Options(), "Migration")
            .ToArray());
    }

    [Fact]
    public void Optional_failure_falls_through_without_owning_the_key()
    {
        var options = Options();
        options.Optional = true;
        var source = new SecretApiConfigurationSource(
            options,
            new Dictionary<string, string>
            {
                ["Connection"] = "{{secret:DatabasePassword}}",
            },
            _ => new RecordingProvider(_ => throw new HttpRequestException("offline")));

        var provider = source.Build(new ConfigurationBuilder());
        provider.Load();

        Assert.False(provider.TryGet("Connection", out _));
    }

    [Fact]
    public void Production_rejects_optional_or_disabled_resolution()
    {
        var optionalConfiguration = Configuration(optional: true, enabled: null);
        var disabledConfiguration = Configuration(optional: false, enabled: false);

        Assert.Throws<InvalidOperationException>(() =>
            optionalConfiguration.AddSecretApiProvider());
        Assert.Throws<InvalidOperationException>(() =>
            disabledConfiguration.AddSecretApiProvider());
    }

    [Fact]
    public void Production_rejects_insecure_secret_transport()
    {
        var configuration = Configuration(optional: false, enabled: true);
        configuration["SecretProvider:BaseAddress"] = "http://127.0.0.1:1/";
        configuration["SecretProvider:AllowInsecureHttp"] = "true";

        var error = Assert.Throws<InvalidOperationException>(() =>
            configuration.AddSecretApiProvider());

        Assert.Contains("only be enabled outside production", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData((int)UnixFileMode.UserRead, true)]
    [InlineData((int)(UnixFileMode.UserRead | UnixFileMode.UserWrite), true)]
    [InlineData((int)(UnixFileMode.UserRead | UnixFileMode.GroupRead), true)]
    [InlineData((int)(UnixFileMode.UserRead | UnixFileMode.OtherRead), false)]
    [InlineData((int)(UnixFileMode.UserRead | UnixFileMode.GroupWrite), false)]
    [InlineData((int)(UnixFileMode.UserRead | UnixFileMode.UserExecute), false)]
    [InlineData((int)(UnixFileMode.UserRead | UnixFileMode.SetGroup), false)]
    [InlineData((int)UnixFileMode.UserWrite, false)]
    public void Unix_private_key_permission_policy_is_least_privilege(
        int rawMode,
        bool expected)
    {
        Assert.Equal(
            expected,
            MachineCertificateLoader.IsSecureUnixFileMode((UnixFileMode)rawMode));
    }

    [Fact]
    public void Pkcs12_file_loading_uses_an_accessible_private_key_without_persisting_it()
    {
        var certificatePath = Path.Combine(
            Path.GetTempPath(),
            $"secret-provider-client-{Guid.NewGuid():N}.pfx");
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        try
        {
            File.WriteAllBytes(certificatePath, CreateClientCertificate(password));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    certificatePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            using var certificate = MachineCertificateLoader.Load(new MachineCertificateOptions
            {
                Path = certificatePath,
                Password = password,
            });

            Assert.True(certificate.HasPrivateKey);
            Assert.True(certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow);
        }
        finally
        {
            File.Delete(certificatePath);
        }
    }

    [Fact]
    public void Linux_pkcs12_loading_rejects_a_world_readable_private_key_file()
    {
        if (!OperatingSystem.IsLinux()
            && !OperatingSystem.IsMacOS()
            && !OperatingSystem.IsFreeBSD())
        {
            return;
        }

        var certificatePath = Path.Combine(
            Path.GetTempPath(),
            $"secret-provider-client-permissions-{Guid.NewGuid():N}.pfx");
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        try
        {
            File.WriteAllBytes(certificatePath, CreateClientCertificate(password));
            File.SetUnixFileMode(
                certificatePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);

            var error = Assert.Throws<InvalidOperationException>(() =>
                MachineCertificateLoader.Load(new MachineCertificateOptions
                {
                    Path = certificatePath,
                    Password = password,
                }));

            Assert.Contains("must not be", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(certificatePath))
            {
                File.SetUnixFileMode(
                    certificatePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.Delete(certificatePath);
            }
        }
    }

    [Fact]
    public void Linux_pkcs12_loading_checks_the_target_of_a_projected_secret_symlink()
    {
        if (!OperatingSystem.IsLinux()
            && !OperatingSystem.IsMacOS()
            && !OperatingSystem.IsFreeBSD())
        {
            return;
        }

        var directory = Path.Combine(
            Path.GetTempPath(),
            $"secret-provider-projected-{Guid.NewGuid():N}");
        var targetPath = Path.Combine(directory, "projected-client.pfx");
        var linkPath = Path.Combine(directory, "client.pfx");
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(targetPath, CreateClientCertificate(password));
            File.SetUnixFileMode(
                targetPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.CreateSymbolicLink(linkPath, Path.GetFileName(targetPath));

            using var certificate = MachineCertificateLoader.Load(new MachineCertificateOptions
            {
                Path = linkPath,
                Password = password,
            });

            Assert.True(certificate.HasPrivateKey);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Resolved_configuration_binds_options_without_service_startup_mutation()
    {
        var options = Options();
        var resolver = new RecordingProvider(_ => "resolved-password");
        var builder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Password"] = "{{secret:DatabasePassword}}",
            })
            .Add(new SecretApiConfigurationSource(
                options,
                new Dictionary<string, string>
                {
                    ["Database:Password"] = "{{secret:DatabasePassword}}",
                },
                _ => resolver));
        var configuration = builder.Build();
        var services = new ServiceCollection();
        services.Configure<DatabaseOptions>(configuration.GetSection("Database"));

        using var serviceProvider = services.BuildServiceProvider();

        Assert.Equal(
            "resolved-password",
            serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Password);
        Assert.Single(resolver.Requests);
    }

    private static SecretApiOptions Options() => new()
    {
        References = new Dictionary<string, SecretAliasOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["DatabasePassword"] = new()
            {
                Namespace = "database",
                Key = "password",
            },
        },
    };

    private static ConfigurationManager Configuration(bool optional, bool? enabled)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Production",
            ["Connection"] = "{{secret:DatabasePassword}}",
            ["SecretProvider:Enabled"] = enabled?.ToString(),
            ["SecretProvider:Optional"] = optional.ToString(),
            ["SecretProvider:References:DatabasePassword:Namespace"] = "database",
            ["SecretProvider:References:DatabasePassword:Key"] = "password",
        });
        return configuration;
    }

    private static byte[] CreateClientCertificate(string password)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN=secret-provider-test-{Guid.NewGuid():N}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: false,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature,
            critical: true));
        var usages = new OidCollection
        {
            new("1.3.6.1.5.5.7.3.2"),
        };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            usages,
            critical: true));

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1));
        return certificate.Export(X509ContentType.Pkcs12, password);
    }

    private sealed class RecordingProvider(Func<SecretReference, string> resolver)
        : ISecretProvider
    {
        public List<SecretReference> Requests { get; } = [];

        public Task<string> GetSecretAsync(
            SecretReference reference,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(reference);
            return Task.FromResult(resolver(reference));
        }
    }

    private sealed class ReloadableConfigurationSource(
        IReadOnlyDictionary<string, string?> values) : IConfigurationSource
    {
        public ReloadableConfigurationProvider? Provider { get; private set; }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            Provider = new ReloadableConfigurationProvider(values);
            return Provider;
        }
    }

    private sealed class ReloadableConfigurationProvider : ConfigurationProvider
    {
        public ReloadableConfigurationProvider(IReadOnlyDictionary<string, string?> values)
        {
            Data = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
        }

        public void SetAndReload(string key, string value)
        {
            Data[key] = value;
            OnReload();
        }
    }

    private sealed class DatabaseOptions
    {
        public string Password { get; set; } = string.Empty;
    }
}
