using Microsoft.AspNetCore.Http;
using Comment.Application.Providers.Storage;
using StorageProvider;
using System.IO;
using System;
using System.Threading.Tasks;

namespace Comment.Infra.Providers.Storage
{
    public class Storage : IStorage
    {
        private readonly StorageConfiguration _config;
        private readonly IStorageProviderService _storageService;

        public Storage(StorageConfiguration config, IStorageProviderService storageService)
        {
            _config = config;
            _storageService = storageService;
        }
        
        public async Task<string> UploadComments(int userId, IFormFile file)
            => await upload(userId, _config.Comments, file);
        private async Task<string> upload(int userId, string container, IFormFile file)
        {
            if (file is null) return null;

            using (var stream = file.OpenReadStream())
            {
                if (file is null) return null;
                return await _storageService.UploadStream(
                    file: stream,
                    userId: userId,
                    fileName: $"{file.FileName}{Guid.NewGuid()}",
                    fileNamespace: container,
                    fileMimeType: file.ContentType,
                    fileExtension: Path.GetExtension(file.FileName));
            }
        }
    }

}
