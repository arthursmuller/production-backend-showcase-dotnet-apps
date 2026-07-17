using Comment.Domain.AggregatesModel;
using System.Threading.Tasks;

namespace Comment.Application.Abstractions.Persistence
{
    public interface ICommentRepository
    {
        Task<CommentPost> AddAsync(CommentPost comment);
        Task<CommentPost> GetByIdAsync(int id);
        Task<CommentPost> RemoveOwnedAsync(int id, int ownerUserId);
        Task<CommentPost> SaveUpdatedAsync(CommentPost comment);
    }
}
