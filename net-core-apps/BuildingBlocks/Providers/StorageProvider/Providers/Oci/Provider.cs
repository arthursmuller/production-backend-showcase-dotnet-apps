using System;
using System.IO;
using System.Security;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Oci.Common;
using Oci.Common.Auth;
using Oci.ObjectstorageService;
using Oci.ObjectstorageService.Requests;
using Oci.Common.Model;
using Oci.ObjectstorageService.Models;

namespace StorageProvider.Providers.Oci;

/// <summary>
/// OCI Object Storage backend – production. Activated when
/// <c>Storage:OciConnection</c> has <c>"enabled":"true"</c>.
/// </summary>
public sealed class OciObjectStorageProvider : IStorageProvider, IDisposable
{
    private readonly OciObjectStorageOptions _opts;
    private readonly ObjectStorageClient _client;
    private readonly string _bucket;
    private readonly string _namespace;
    private readonly SemaphoreSlim _bucketValidationLock = new(1, 1);
    private bool _privateBucketValidated;

    public OciObjectStorageProvider(OciObjectStorageOptions opts)
    {
        _opts = opts;
        var region = Region.FromRegionId(_opts.Region);
        _client = new ObjectStorageClient(BuildAuthenticationProvider(_opts, region));
        _bucket = _opts.BucketName;
        _namespace = _opts.Namespace;
    }

    public async Task<StorageObjectWriteResult> PutObject(
        string fileNamespace,
        string fileName,
        Stream file,
        int? userId,
        string? fileMimeType,
        string? fileExtension,
        CancellationToken cancellationToken)
    {
        var key = StorageObjectKey.Build(fileNamespace, userId, fileName, fileExtension);
        await EnsurePrivateBucketAsync(cancellationToken);
        var length = StorageObjectKey.GetValidatedLength(file, _opts.MaximumUploadBytes);
        var mimeType = StorageObjectKey.ValidateMimeType(fileMimeType);
        var version = await PutAsync(key, file, length, mimeType, cancellationToken);
        return new(
            _opts.UsesPrivateAccess ? key : BuildPublicUrl(key),
            version);
    }

    public async Task<bool> ObjectExists(
        string fileNamespace,
        string fileName,
        int? userId,
        string? fileExtension,
        CancellationToken cancellationToken)
    {
        var key = StorageObjectKey.Build(fileNamespace, userId, fileName, fileExtension);
        await EnsurePrivateBucketAsync(cancellationToken);
        try
        {
            await _client.HeadObject(new HeadObjectRequest
            {
                NamespaceName = _namespace,
                BucketName = _bucket,
                ObjectName = key,
            }, cancellationToken: cancellationToken);
            return true;
        }
        catch (OciException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task DownloadObject(
        string fileNamespace,
        string fileName,
        int? userId,
        string? fileExtension,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("The storage download destination must be writable.", nameof(destination));
        var key = StorageObjectKey.Build(fileNamespace, userId, fileName, fileExtension);
        await EnsurePrivateBucketAsync(cancellationToken);
        var response = await _client.GetObject(new GetObjectRequest
        {
            NamespaceName = _namespace,
            BucketName = _bucket,
            ObjectName = key,
        }, cancellationToken: cancellationToken);
        await using (response.InputStream)
            await response.InputStream.CopyToAsync(destination, cancellationToken);
        if (destination.CanSeek)
            destination.Position = 0;
    }

    public async Task<StorageObjectWriteResult> CopyObject(
        string sourceNamespace,
        string sourceFileName,
        string destinationNamespace,
        string destinationFileName,
        int? userId,
        string fileMimeType,
        string? sourceExtension,
        string? destinationExtension,
        long contentLength,
        CancellationToken cancellationToken)
    {
        await using var temporary = CreateTemporaryStream();
        await DownloadObject(
            sourceNamespace,
            sourceFileName,
            userId,
            sourceExtension,
            temporary,
            cancellationToken);
        if (temporary.Length != contentLength)
            throw new InvalidDataException("The source object length changed before promotion.");
        return await PutObject(
            destinationNamespace,
            destinationFileName,
            temporary,
            userId,
            fileMimeType,
            destinationExtension,
            cancellationToken);
    }

    public async Task DeleteObject(
        string fileNamespace,
        string fileName,
        int? userId,
        string? fileExtension,
        CancellationToken cancellationToken)
    {
        var key = StorageObjectKey.Build(fileNamespace, userId, fileName, fileExtension);
        await EnsurePrivateBucketAsync(cancellationToken);
        try
        {
            await _client.DeleteObject(new DeleteObjectRequest
            {
                NamespaceName = _namespace,
                BucketName = _bucket,
                ObjectName = key,
            }, cancellationToken: cancellationToken);
        }
        catch (OciException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // Exact-object cleanup is deliberately idempotent.
        }
    }

    private async Task<string> PutAsync(
        string key,
        Stream content,
        long length,
        string mime,
        CancellationToken cancellationToken)
    {
        var response = await _client.PutObject(new PutObjectRequest
        {
            NamespaceName = _namespace,
            BucketName = _bucket,
            ObjectName = key,
            ContentLength = length,
            ContentType = mime,
            PutObjectBody = content,
        }, cancellationToken: cancellationToken);
        return response.ETag ?? "not-reported";
    }

    private string BuildPublicUrl(string key)
    {
        var encoded = Uri.EscapeDataString(key).Replace("%2F", "/");
        return $"{_opts.PublicBaseUrl!.TrimEnd('/')}/{encoded}";
    }

    private async Task EnsurePrivateBucketAsync(CancellationToken cancellationToken)
    {
        if (!_opts.UsesPrivateAccess || _privateBucketValidated)
            return;

        await _bucketValidationLock.WaitAsync(cancellationToken);
        try
        {
            if (_privateBucketValidated)
                return;

            var response = await _client.GetBucket(new GetBucketRequest
            {
                NamespaceName = _namespace,
                BucketName = _bucket,
            }, cancellationToken: cancellationToken);
            if (response.Bucket?.PublicAccessType !=
                Bucket.PublicAccessTypeEnum.NoPublicAccess)
            {
                throw new InvalidOperationException(
                    "The configured private OCI bucket permits public object access.");
            }

            _privateBucketValidated = true;
        }
        finally
        {
            _bucketValidationLock.Release();
        }
    }

    private static FileStream CreateTemporaryStream() => new(
        Path.Combine(Path.GetTempPath(), $"inspacy-storage-{Guid.NewGuid():N}.tmp"),
        FileMode.CreateNew,
        FileAccess.ReadWrite,
        FileShare.None,
        64 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);

    public void Dispose()
    {
        _bucketValidationLock.Dispose();
        _client.Dispose();
    }

    private static IBasicAuthenticationDetailsProvider BuildAuthenticationProvider(
        OciObjectStorageOptions options,
        Region region)
    {
        if (options.AuthMode.Equals("ResourcePrincipal", StringComparison.OrdinalIgnoreCase))
        {
            var provider = ResourcePrincipalAuthenticationDetailsProvider.GetProvider();
            provider.Region = region;
            return provider;
        }
        if (options.AuthMode.Equals("InstancePrincipal", StringComparison.OrdinalIgnoreCase))
        {
            return new InstancePrincipalsAuthenticationDetailsProvider { Region = region };
        }

        if (string.IsNullOrWhiteSpace(options.PrivateKeyPath)
            || !File.Exists(options.PrivateKeyPath))
        {
            throw new InvalidOperationException(
                "OCI ApiKey authentication requires an existing mounted privateKeyPath.");
        }

        SecureString? passphrase = null;
        if (!string.IsNullOrEmpty(options.PrivateKeyPassphrase))
        {
            passphrase = new SecureString();
            foreach (var character in options.PrivateKeyPassphrase)
                passphrase.AppendChar(character);
            passphrase.MakeReadOnly();
        }

        return new SimpleAuthenticationDetailsProvider
        {
            TenantId = options.TenancyOcid,
            UserId = options.UserOcid,
            Fingerprint = options.Fingerprint,
            Region = region,
            PrivateKeySupplier = new FilePrivateKeySupplier(options.PrivateKeyPath, passphrase),
        };
    }
}
