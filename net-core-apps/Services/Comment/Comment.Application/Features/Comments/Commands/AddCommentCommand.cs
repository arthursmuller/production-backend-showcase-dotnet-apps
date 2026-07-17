using Comment.Application.Abstractions;
using Comment.Application.Abstractions.Persistence;
using Comment.Application.Dtos;
using Comment.Application.Extensions;
using Comment.Application.Providers.Storage;
using Comment.Domain.AggregatesModel;
using System.Threading.Tasks;

namespace Comment.Application.Features.Comments.Commands
{
    public record AddCommentCommand(
        ICommentRepository _comments,
        IIdentity _auth,
        IStorage _storage) : Command<AddCommentPostDto, CommentPost>
    {
        public override async Task<CommentPost> Handle(AddCommentPostDto dto)
        {
            var userId = _auth.User.Id;
            var mediaItems = await dto.ResolveMediaItems(
                MediaConversionHelper.FromStringUploader(file => _storage.UploadComments(userId, file)));

            var comment = new CommentPost(
                _auth.User.ToDomainUser(),
                dto.Description,
                dto.GifUrl,
                dto.IdOfRelatedEntity,
                dto.TypeOfRelatedEntity,
                dto.Categories,
                dto.VisibilityLevel,
                mediaItems,
                dto.Mentions);

            return await _comments.AddAsync(comment);
        }
    }
}
