using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StorageProvider;

public sealed record StorageObjectWriteResult(
    string Url,
    string ObjectVersion);

public interface IStorageProvider
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
