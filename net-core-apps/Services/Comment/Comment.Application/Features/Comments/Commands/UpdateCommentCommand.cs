using Comment.Application.Abstractions;
using Comment.Application.Abstractions.Persistence;
using Comment.Application.Dtos;
using Comment.Application.Providers.Storage;
using Comment.Domain.AggregatesModel;
using System.Threading.Tasks;

namespace Comment.Application.Features.Comments.Commands
{
    public record UpdateCommentCommand(
        ICommentRepository _comments,
        IIdentity _auth,
        IStorage _storage) : Command<UpdateCommentDto, CommentPost>
    {
        public override async Task<CommentPost> Handle(UpdateCommentDto dto)
        {
            var existing = await _comments.GetByIdAsync(dto.Id);
            if (existing is null) return null;

            var mediaItems = await dto.ResolveMediaItemsUpdate(
                MediaConversionHelper.FromStringUploader(
                    file => _storage.UploadComments(_auth.User.Id, file)));

            existing.Update(
                dto.Description,
                dto.GifUrl,
                dto.VisibilityLevel,
                dto.Categories,
                dto.CategoriesToRemove,
                mediaItems,
                dto.Mentions);

            return await _comments.SaveUpdatedAsync(existing);
        }
    }
}
