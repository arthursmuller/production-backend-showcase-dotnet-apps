using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using InspacyApiHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApiHost.UnitTests;

public sealed class HostStartupConfigurationTests
{
    [Theory]
    [InlineData("{{secret:not-closed", "Malformed")]
    [InlineData("{{secret:MissingAlias}}", "does not define secret alias")]
    public void Production_host_rejects_malformed_or_unmapped_secret_markers(
        string configuredValue,
        string expectedMessage)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ApiBuilder.Create(
            CreateOptions(),
            HostArguments(
                "Production",
                $"--HostStartupProbe:Value={configuredValue}")));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_host_rejects_an_unresolved_placeholder_before_service_binding()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ApiBuilder.Create(
            CreateOptions(),
            HostArguments(
                "Production",
                "--HostStartupProbe:Value={{secret:HostStartupValue}}",
                "--SecretProvider:References:HostStartupValue:Namespace=host-startup",
                "--SecretProvider:References:HostStartupValue:Key=value",
                "--SecretProvider:References:HostStartupValue:Roles:0=JobWorker")));

        Assert.Contains("BaseAddress is not configured", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_host_rejects_a_missing_client_certificate_before_network_io()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ApiBuilder.Create(
            CreateOptions(),
            HostArguments(
                "Production",
                "--HostStartupProbe:Value={{secret:HostStartupValue}}",
                "--SecretProvider:BaseAddress=https://secret-provider.invalid/",
                "--SecretProvider:References:HostStartupValue:Namespace=host-startup",
                "--SecretProvider:References:HostStartupValue:Key=value",
                "--SecretProvider:References:HostStartupValue:Roles:0=JobWorker")));

        Assert.Contains("Certificate must specify", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_host_rejects_an_insecure_secret_provider_bootstrap()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ApiBuilder.Create(
            CreateOptions(),
            HostArguments(
                "Production",
                "--HostStartupProbe:Value={{secret:HostStartupValue}}",
                "--SecretProvider:BaseAddress=http://127.0.0.1:1/",
                "--SecretProvider:AllowInsecureHttp=true",
                "--SecretProvider:References:HostStartupValue:Namespace=host-startup",
                "--SecretProvider:References:HostStartupValue:Key=value",
                "--SecretProvider:References:HostStartupValue:Roles:0=JobWorker")));

        Assert.Contains("only be enabled outside production", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Environment_and_command_line_precedence_reaches_typed_options_and_consumers()
    {
        var environmentValue = $"environment-{Guid.NewGuid():N}";
        var commandValue = $"command-{Guid.NewGuid():N}";
        using var environment = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["HostStartupProbe__Value"] = environmentValue,
        });

        var environmentHost = ApiBuilder.Create(
            CreateOptions(),
            HostArguments("Development"));
        await using (var environmentApp = environmentHost.Build())
        {
            Assert.Equal(
                environmentValue,
                environmentApp.Services.GetRequiredService<HostStartupProbeConsumer>().Value);
        }

        var commandHost = ApiBuilder.Create(
            CreateOptions(),
            HostArguments(
                "Development",
                $"--HostStartupProbe:Value={commandValue}"));
        await using var commandApp = commandHost.Build();

        Assert.Equal(
            commandValue,
            commandApp.Services.GetRequiredService<HostStartupProbeConsumer>().Value);
    }

    [Fact]
    public async Task Resolved_options_are_one_startup_snapshot_and_restart_observes_rotation()
    {
        await using var secretApi = await SecretApiStub.StartAsync();
        using var certificate = ClientCertificateFile.Create();
        var firstValue = $"snapshot-{Guid.NewGuid():N}";
        var secondValue = $"rotated-{Guid.NewGuid():N}";
        secretApi.Value = firstValue;

        using var environment = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["HostStartupProbe__Value"] = "{{secret:HostStartupValue}}",
            ["SecretProvider__BaseAddress"] = secretApi.BaseAddress.ToString(),
            ["SecretProvider__AllowInsecureHttp"] = "true",
            ["SecretProvider__Certificate__Path"] = certificate.Path,
            ["SecretProvider__Certificate__Password"] = certificate.Password,
            ["SecretProvider__References__HostStartupValue__Namespace"] = "host-startup",
            ["SecretProvider__References__HostStartupValue__Key"] = "value",
            ["SecretProvider__References__HostStartupValue__Roles__0"] = "JobWorker",
        });

        var firstHost = ApiBuilder.Create(CreateOptions(), HostArguments("Development"));
        firstHost.Builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using (var firstApp = firstHost.Build())
        {
            await firstApp.StartAsync(TestContext.Current.CancellationToken);
            var consumer = firstApp.Services.GetRequiredService<HostStartupProbeConsumer>();
            var monitor = firstApp.Services.GetRequiredService<IOptionsMonitor<HostStartupProbeOptions>>();

            Assert.Equal(firstValue, consumer.Value);
            Assert.Equal(firstValue, monitor.CurrentValue.Value);
            Assert.Equal(1, secretApi.Requests);

            secretApi.Value = secondValue;

            Assert.Equal(firstValue, consumer.Value);
            Assert.Equal(firstValue, monitor.CurrentValue.Value);
            Assert.Equal(1, secretApi.Requests);
            await firstApp.StopAsync(TestContext.Current.CancellationToken);
        }

        var restartedHost = ApiBuilder.Create(CreateOptions(), HostArguments("Development"));
        restartedHost.Builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var restartedApp = restartedHost.Build();
        await restartedApp.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            secondValue,
            restartedApp.Services.GetRequiredService<HostStartupProbeConsumer>().Value);
        Assert.Equal(2, secretApi.Requests);

        await restartedApp.StopAsync(TestContext.Current.CancellationToken);
    }

    private static string[] HostArguments(string environment, params string[] additional) =>
    [
        $"--environment={environment}",
        "--Runtime:Role=JobWorker",
        .. additional,
    ];

    private static ApiOptions CreateOptions() => new()
    {
        AppName = "HostStartupProbe",
        EntryAssembly = typeof(HostStartupConfigurationTests).Assembly,
        AuthPolicies = [],
        AllowedCorsDomains = ["https://client.test"],
        WithApplication = false,
        WithInfra = false,
        WithCqrs = new CqrsOptions { Enabled = false },
        WithControllers = false,
        WithRouteGroups = false,
        WithBroker = false,
        WithConsumers = false,
        WithJobs = false,
        ConfigureServices = builder => builder.Services
            .AddOptions<HostStartupProbeOptions>()
            .Bind(builder.Configuration.GetSection("HostStartupProbe"))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Value), "Value is required.")
            .ValidateOnStart()
            .Services
            .AddSingleton<HostStartupProbeConsumer>(),
    };

    private sealed class HostStartupProbeOptions
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class HostStartupProbeConsumer(IOptions<HostStartupProbeOptions> options)
    {
        public string Value { get; } = options.Value.Value;
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues;

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            _originalValues = values.Keys.ToDictionary(
                key => key,
                Environment.GetEnvironmentVariable,
                StringComparer.Ordinal);
            foreach (var entry in values)
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
        }

        public void Dispose()
        {
            foreach (var entry in _originalValues)
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
        }
    }

    private sealed class ClientCertificateFile : IDisposable
    {
        private ClientCertificateFile(string path, string password)
        {
            Path = path;
            Password = password;
        }

        public string Path { get; }
        public string Password { get; }

        public static ClientCertificateFile Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"api-host-client-{Guid.NewGuid():N}.pfx");
            var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(
                $"CN=api-host-client-{Guid.NewGuid():N}",
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
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.2") },
                critical: true));
            using var generated = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddHours(1));
            File.WriteAllBytes(path, generated.Export(X509ContentType.Pkcs12, password));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            return new ClientCertificateFile(path, password);
        }

        public void Dispose() => File.Delete(Path);
    }

    private sealed class SecretApiStub : IAsyncDisposable
    {
        private readonly WebApplication _application;
        private int _requests;

        private SecretApiStub(WebApplication application, Uri baseAddress)
        {
            _application = application;
            BaseAddress = baseAddress;
        }

        public Uri BaseAddress { get; }
        public string Value { get; set; } = string.Empty;
        public int Requests => Volatile.Read(ref _requests);

        public static async Task<SecretApiStub> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(HostStartupConfigurationTests).Assembly.GetName().Name,
                EnvironmentName = "Development",
            });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var application = builder.Build();
            SecretApiStub? stub = null;
            application.MapGet(
                "/api/secrets/{secretNamespace}/{key}",
                (string secretNamespace, string key) =>
                {
                    Interlocked.Increment(ref stub!._requests);
                    return Results.Json(new { value = stub.Value });
                });

            try
            {
                await application.StartAsync(TestContext.Current.CancellationToken);
                var addresses = application.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()?.Addresses
                    ?? throw new InvalidOperationException("The secret stub did not expose an address.");
                stub = new SecretApiStub(
                    application,
                    new Uri(addresses.Single(address => address.StartsWith("http", StringComparison.Ordinal))));
                return stub;
            }
            catch
            {
                await application.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _application.StopAsync();
            await _application.DisposeAsync();
        }
    }
}
