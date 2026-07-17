using System.IO;
using System.Threading.Tasks;

namespace StorageProvider;

public interface IStorageProvider
{
    Task<DownloadDto?> Get(string endpoint);
    Task<string> UploadStream(string fileNamespace, string fileName, Stream file, int? userId, string? fileMimeType, string? fileExtension);
    Task<bool> Delete(string endpoint);
}
