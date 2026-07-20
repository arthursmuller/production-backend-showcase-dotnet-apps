using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StorageProvider;

/// <summary>
/// Public surface used by every API. The implementation is selected at
/// startup based on the <c>Storage:Minio</c> and
/// <c>Storage:OciConnection</c> settings. MinIO is intended for local
/// development; OCI Object Storage is the production backend.
/// </summary>
public interface IStorageProviderService
{
    Task<StorageObjectWriteResult> PutObject(
        string fileNamespace,
        string fileName,
        Stream file,
        int? userId,
        string? fileMimeType,
        string? fileExtension,
        CancellationToken cancellationToken);

    Task<bool> ObjectExists(
        string fileNamespace,
        string fileName,
        int? userId,
        string? fileExtension,
        CancellationToken cancellationToken);

    Task DownloadObject(
        string fileNamespace,
        string fileName,
        int? userId,
        string? fileExtension,
        Stream destination,
        CancellationToken cancellationToken);

    Task<StorageObjectWriteResult> CopyObject(
        string sourceNamespace,
        string sourceFileName,
        string destinationNamespace,
        string destinationFileName,
        int? userId,
        string fileMimeType,
        string? sourceExtension,
        string? destinationExtension,
        long contentLength,
        CancellationToken cancellationToken);

    Task DeleteObject(
        string fileNamespace,
        string fileName,
        int? userId,
        string? fileExtension,
        CancellationToken cancellationToken);
}
