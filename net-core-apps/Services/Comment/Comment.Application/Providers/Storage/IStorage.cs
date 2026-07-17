using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace Comment.Application.Providers.Storage
{
    public interface IStorage
    {
        Task<string> UploadComments(int userId, IFormFile file);
    }
}
