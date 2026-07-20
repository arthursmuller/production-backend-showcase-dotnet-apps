using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using StorageProvider;
using StorageProvider.Providers.Minio;
using StorageProvider.Providers.Oci;
using System.Text.Json;
using Xunit;

namespace StorageProvider.UnitTests;

public sealed class StorageSecurityTests
{
    [Fact]
    public void Object_key_is_canonical_and_scoped_to_namespace_and_user()
    {
        var key = StorageObjectKey.Build(
            "comments",
            42,
            "0123456789abcdef0123456789abcdef.png",
            ".png");

        Assert.Equal("comments/42/0123456789abcdef0123456789abcdef.png", key);
    }

    [Theory]
    [InlineData("../comments", "file.png", ".png")]
    [InlineData("Comments", "file.png", ".png")]
    [InlineData("comments", "../file.png", ".png")]
    [InlineData("comments", "file.png", ".jpg")]
    [InlineData("comments", "file.png", ".PNG")]
    public void Unsafe_object_key_inputs_are_rejected(
        string fileNamespace,
        string fileName,
        string extension)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            StorageObjectKey.Build(fileNamespace, 42, fileName, extension));
    }

    [Fact]
    public void Upload_stream_must_be_seekable_and_bounded()
    {
        using var nonSeekable = new NonSeekableStream(new byte[] { 1, 2, 3 });
        using var tooLarge = new MemoryStream(new byte[9]);

        Assert.Throws<ArgumentException>(() =>
            StorageObjectKey.GetValidatedLength(nonSeekable, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StorageObjectKey.GetValidatedLength(tooLarge, 8));
    }

    [Theory]
    [InlineData("image/png\r\nX-Injected: true")]
    [InlineData("applicationoctet-stream")]
    [InlineData("")]
    public void Unsafe_content_types_are_rejected(string mimeType)
    {
        Assert.Throws<ArgumentException>(() => StorageObjectKey.ValidateMimeType(mimeType));
    }

    [Fact]
    public void Minio_may_create_and_publish_a_bucket_only_in_development()
    {
        var development = Configuration(new()
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Storage:Minio:enabled"] = "true",
            ["Storage:Minio:endpoint"] = "127.0.0.1:9000",
            ["Storage:Minio:accessKey"] = "local-access",
            ["Storage:Minio:secretKey"] = "local-secret",
            ["Storage:Minio:bucket"] = "local-media",
            ["Storage:Minio:useSSL"] = "false",
            ["Storage:Minio:publicUrl"] = "http://localhost:9000",
            ["Storage:Minio:createBucket"] = "true",
            ["Storage:Minio:allowPublicRead"] = "true",
        });

        var options = MinioStorageProviderExtensions.BuildOptions(development);

        Assert.NotNull(options);
        Assert.True(options.CreateBucket);
        Assert.True(options.AllowPublicRead);

        var production = Configuration(development.AsEnumerable()
            .ToDictionary(item => item.Key, item => item.Value));
        production["ASPNETCORE_ENVIRONMENT"] = "Production";
        Assert.Throws<InvalidOperationException>(() =>
            MinioStorageProviderExtensions.BuildOptions(production));
    }

    [Fact]
    public void Production_minio_requires_tls_and_private_preprovisioned_bucket()
    {
        var configuration = Configuration(new()
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["Storage:Minio:enabled"] = "true",
            ["Storage:Minio:endpoint"] = "minio.storage.svc:9000",
            ["Storage:Minio:accessKey"] = "mounted-access",
            ["Storage:Minio:secretKey"] = "mounted-secret",
            ["Storage:Minio:bucket"] = "media",
            ["Storage:Minio:useSSL"] = "true",
            ["Storage:Minio:publicUrl"] = "https://media.example.test",
        });

        var options = MinioStorageProviderExtensions.BuildOptions(configuration);

        Assert.NotNull(options);
        Assert.True(options.UseSSL);
        Assert.False(options.CreateBucket);
        Assert.False(options.AllowPublicRead);
    }

    [Fact]
    public void Minio_client_is_container_owned_and_shared_across_provider_scopes()
    {
        var options = new MinioStorageOptions
        {
            Enabled = true,
            Endpoint = "127.0.0.1:9000",
            AccessKey = "local-access",
            SecretKey = "local-secret",
            Bucket = "local-media",
        };
        using var services = new ServiceCollection()
            .ConfigureMinioStorageProvider(options)
            .BuildServiceProvider();
        using var firstScope = services.CreateScope();
        using var secondScope = services.CreateScope();

        var firstClient = firstScope.ServiceProvider.GetRequiredService<IMinioClient>();
        var secondClient = secondScope.ServiceProvider.GetRequiredService<IMinioClient>();
        var firstProvider = firstScope.ServiceProvider.GetRequiredService<IStorageProvider>();
        var secondProvider = secondScope.ServiceProvider.GetRequiredService<IStorageProvider>();

        Assert.Same(firstClient, secondClient);
        Assert.NotSame(firstProvider, secondProvider);
    }

    [Fact]
    public void Oci_defaults_to_resource_principal_and_requires_https_cdn_url()
    {
        var optionsJson = JsonSerializer.Serialize(new
        {
            enabled = true,
            region = "sa-saopaulo-1",
            @namespace = "productionnamespace",
            bucketName = "private-media",
            publicBaseUrl = "https://media.example.test/comments",
        });
        var options = OciStorageProviderExtensions.BuildOptions(Configuration(new()
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["Storage:OciConnection"] = optionsJson,
        }));

        Assert.NotNull(options);
        Assert.Equal("ResourcePrincipal", options.AuthMode);
        Assert.Equal("https://media.example.test/comments", options.PublicBaseUrl);
    }

    [Fact]
    public void Oci_private_mode_rejects_public_url_configuration()
    {
        var privateOptions = JsonSerializer.Serialize(new
        {
            enabled = true,
            region = "sa-saopaulo-1",
            @namespace = "productionnamespace",
            bucketName = "comment-private-media",
            accessMode = "Private",
        });
        var options = OciStorageProviderExtensions.BuildOptions(Configuration(new()
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["Storage:OciConnection"] = privateOptions,
        }));

        Assert.NotNull(options);
        Assert.True(options.UsesPrivateAccess);
        Assert.Null(options.PublicBaseUrl);

        var unsafeOptions = JsonSerializer.Serialize(new
        {
            enabled = true,
            region = "sa-saopaulo-1",
            @namespace = "productionnamespace",
            bucketName = "comment-private-media",
            accessMode = "Private",
            publicBaseUrl = "https://media.example.test/comments",
        });
        Assert.Throws<InvalidOperationException>(() =>
            OciStorageProviderExtensions.BuildOptions(Configuration(new()
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["Storage:OciConnection"] = unsafeOptions,
            })));
    }

    [Fact]
    public void Oci_rejects_inline_private_keys_and_workload_identity_key_material()
    {
        var inlinePem = OciJson(new
        {
            privateKeyPem = "-----BEGIN PRIVATE KEY-----secret",
        });
        var strayKeyMaterial = OciJson(new
        {
            tenancyOcid = "ocid1.tenancy.example",
        });

        Assert.Throws<InvalidOperationException>(() =>
            OciStorageProviderExtensions.BuildOptions(Configuration(new()
            {
                ["Storage:OciConnection"] = inlinePem,
            })));
        Assert.Throws<InvalidOperationException>(() =>
            OciStorageProviderExtensions.BuildOptions(Configuration(new()
            {
                ["Storage:OciConnection"] = strayKeyMaterial,
            })));
    }

    private static string OciJson(object additional)
    {
        var values = new Dictionary<string, object?>
        {
            ["enabled"] = true,
            ["region"] = "sa-saopaulo-1",
            ["namespace"] = "productionnamespace",
            ["bucketName"] = "private-media",
            ["publicBaseUrl"] = "https://media.example.test/comments",
        };
        foreach (var property in additional.GetType().GetProperties())
            values[property.Name] = property.GetValue(additional);
        return JsonSerializer.Serialize(values);
    }

    private static ConfigurationManager Configuration(
        Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(values);
        return configuration;
    }

    private sealed class NonSeekableStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
