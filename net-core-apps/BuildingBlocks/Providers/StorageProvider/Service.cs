using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StorageProvider;

public class StorageProviderService : IStorageProviderService
{
    private readonly IStorageProvider _storageProvider;

    public StorageProviderService(IStorageProvider client) => _storageProvider = client;

    public Task<StorageObjectWriteResult> PutObject(
        string fileNamespace,
        string fileName,
        Stream file,
        int? userId,
        string? fileMimeType,
        string? fileExtension,
        CancellationToken cancellationToken) =>
        _storageProvider.PutObject(
            fileNamespace,
            fileName,
            file,
            userId,
            fileMimeType,
            fileExtension,
            cancellationToken);

    public Task<bool> ObjectExists(
        string fileNamespace,
        string fileName,
        int? userId,
        string? fileExtension,
        CancellationToken cancellationToken) =>
        _storageProvider.ObjectExists(
            fileNamespace,
            fileName,
            userId,
            fileExtension,
            cancellationToken);

    public Task DownloadObject(
        string fileNamespace,
        string fileName,
        int? userId,
        string? fileExtension,
        Stream destination,
        CancellationToken cancellationToken) =>
        _storageProvider.DownloadObject(
            fileNamespace,
            fileName,
            userId,
            fileExtension,
            destination,
            cancellationToken);

    public Task<StorageObjectWriteResult> CopyObject(
        string sourceNamespace,
        string sourceFileName,
        string destinationNamespace,
        string destinationFileName,
        int? userId,
        string fileMimeType,
        string? sourceExtension,
        string? destinationExtension,
        long contentLength,
        CancellationToken cancellationToken) =>
        _storageProvider.CopyObject(
            sourceNamespace,
            sourceFileName,
            destinationNamespace,
            destinationFileName,
            userId,
            fileMimeType,
            sourceExtension,
            destinationExtension,
            contentLength,
            cancellationToken);

    public Task DeleteObject(
        string fileNamespace,
        string fileName,
        int? userId,
        string? fileExtension,
        CancellationToken cancellationToken) =>
        _storageProvider.DeleteObject(
            fileNamespace,
            fileName,
            userId,
            fileExtension,
            cancellationToken);
}
