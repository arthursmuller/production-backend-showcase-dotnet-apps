using System.IO;
using System.Threading.Tasks;

namespace StorageProvider;

public class StorageProviderService : IStorageProviderService
{
    private readonly IStorageProvider _storageProvider;

    public StorageProviderService(IStorageProvider client) => _storageProvider = client;

    public Task<DownloadDto?> Get(string endpoint) => _storageProvider.Get(endpoint);

    public Task<bool> Delete(string endpoint) => _storageProvider.Delete(endpoint);

    public Task<string> UploadStream(string fileNamespace, string fileName, Stream file, int? userId = null, string? fileMimeType = null, string? fileExtension = null)
        => _storageProvider.UploadStream(fileNamespace, fileName, file, userId, fileMimeType, fileExtension);
}
