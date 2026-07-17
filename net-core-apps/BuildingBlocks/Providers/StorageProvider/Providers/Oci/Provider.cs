using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Oci.Common;
using Oci.Common.Auth;
using Oci.ObjectstorageService;
using Oci.ObjectstorageService.Requests;

namespace StorageProvider.Providers.Oci;

/// <summary>
/// OCI Object Storage backend – production. Activated when
/// <c>OCI_CONNECTION</c> JSON env has <c>"enabled":"true"</c>.
/// </summary>
public class OciObjectStorageProvider : IStorageProvider
{
    private readonly OciObjectStorageOptions _opts;
    private readonly ObjectStorageClient _client;
    private readonly string _bucket;
    private readonly string _namespace;

    public OciObjectStorageProvider(OciObjectStorageOptions opts)
    {
        _opts = opts;

        var keyPath = _opts.PrivateKeyPath;
        if (string.IsNullOrEmpty(keyPath) && !string.IsNullOrEmpty(_opts.PrivateKeyPem))
        {
            keyPath = Path.Combine(Path.GetTempPath(), $"oci-{Guid.NewGuid():N}.pem");
            File.WriteAllText(keyPath, _opts.PrivateKeyPem);
        }

        if (string.IsNullOrEmpty(keyPath) || !File.Exists(keyPath))
            throw new InvalidOperationException(
                "OCI Object Storage provider requires privateKeyPath or privateKeyPem in OCI_CONNECTION.");

        SecureString? passphrase = null;
        if (!string.IsNullOrEmpty(_opts.PrivateKeyPassphrase))
        {
            passphrase = new SecureString();
            foreach (var c in _opts.PrivateKeyPassphrase) passphrase.AppendChar(c);
            passphrase.MakeReadOnly();
        }

        var auth = new SimpleAuthenticationDetailsProvider
        {
            TenantId           = _opts.TenancyOcid,
            UserId             = _opts.UserOcid,
            Fingerprint        = _opts.Fingerprint,
            Region             = Region.FromRegionId(_opts.Region),
            PrivateKeySupplier = new FilePrivateKeySupplier(keyPath, passphrase),
        };

        _client    = new ObjectStorageClient(auth);
        _bucket    = _opts.BucketName;
        _namespace = _opts.Namespace;
    }

    public async Task<DownloadDto?> Get(string endpoint)
    {
        var key = ResolveKey(endpoint);
        if (string.IsNullOrEmpty(key)) return null;

        try
        {
            var res = await _client.GetObject(new GetObjectRequest
            {
                NamespaceName = _namespace,
                BucketName    = _bucket,
                ObjectName    = key,
            });

            if (res?.InputStream == null) return null;

            using var ms = new MemoryStream();
            await res.InputStream.CopyToAsync(ms);

            return new DownloadDto
            {
                Blob       = ms.ToArray(),
                FileName   = Path.GetFileNameWithoutExtension(key),
                Extension  = Path.GetExtension(key)?.TrimStart('.'),
                MimeType   = res.ContentType,
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> UploadStream(
        string fileNamespace,
        string fileName,
        Stream file,
        int? userId,
        string? fileMimeType,
        string? fileExtension)
    {
        var key = BuildKey(fileNamespace, userId, fileName, fileExtension);
        await PutAsync(key, file ?? Stream.Null, fileMimeType);
        return BuildPublicUrl(key);
    }

    public async Task<bool> Delete(string endpoint)
    {
        var key = ResolveKey(endpoint);
        if (string.IsNullOrEmpty(key)) return false;

        try
        {
            await _client.DeleteObject(new DeleteObjectRequest
            {
                NamespaceName = _namespace,
                BucketName    = _bucket,
                ObjectName    = key,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task PutAsync(string key, Stream content, string? mime)
    {
        if (content.CanSeek) content.Position = 0;

        await _client.PutObject(new PutObjectRequest
        {
            NamespaceName = _namespace,
            BucketName    = _bucket,
            ObjectName    = key,
            ContentType   = mime ?? "application/octet-stream",
            PutObjectBody = content,
        });
    }

    private static string BuildKey(string ns, int? userId, string fileName, string? extension)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        var ext = !string.IsNullOrWhiteSpace(extension)
            ? extension!.TrimStart('.')
            : Path.GetExtension(fileName ?? string.Empty).TrimStart('.');

        var name = string.IsNullOrWhiteSpace(ext) ? baseName : $"{baseName}.{ext}";
        if (string.IsNullOrWhiteSpace(name)) name = Guid.NewGuid().ToString("N");

        return string.Join("/", new[] { ns, userId?.ToString(), name }
            .Where(s => !string.IsNullOrWhiteSpace(s))!);
    }

    private string? ResolveKey(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return null;

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            var marker = $"/b/{_bucket}/o/";
            var idx    = uri.AbsolutePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return Uri.UnescapeDataString(uri.AbsolutePath.Substring(idx + marker.Length));

            if (!string.IsNullOrEmpty(_opts.PublicBaseUrl) &&
                endpoint.StartsWith(_opts.PublicBaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(endpoint.Substring(_opts.PublicBaseUrl!.TrimEnd('/').Length).TrimStart('/'));
            }
        }

        return endpoint;
    }

    private string BuildPublicUrl(string key)
    {
        var encoded = Uri.EscapeDataString(key).Replace("%2F", "/");
        if (!string.IsNullOrEmpty(_opts.PublicBaseUrl))
            return $"{_opts.PublicBaseUrl!.TrimEnd('/')}/{encoded}";

        return $"https://objectstorage.{_opts.Region}.oraclecloud.com/n/{_namespace}/b/{_bucket}/o/{encoded}";
    }
}
