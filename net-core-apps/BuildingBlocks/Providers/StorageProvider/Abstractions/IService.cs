using System.IO;
using System.Threading.Tasks;

namespace StorageProvider;

/// <summary>
/// Public surface used by every API. The implementation is selected at
/// startup based on the JSON connection envs (MINIO_CONNECTION /
/// OCI_CONNECTION). MinIO is intended for local development; OCI Object
/// Storage is the production backend.
/// </summary>
public interface IStorageProviderService
{
    Task<DownloadDto?> Get(string endpoint);
    Task<string> UploadStream(string fileNamespace, string fileName, Stream file, int? userId = null, string? fileMimeType = null, string? fileExtension = null);
    Task<bool> Delete(string endpoint);
}
