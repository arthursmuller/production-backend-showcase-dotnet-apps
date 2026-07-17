using Posts.Provider.Dtos;
using StarterEntities;

namespace Posts.Provider.Abstractions
{
    public interface IBroker
    {
        Guid ShareCreated(UpdateCommentCountDto dto, string postType);
        Guid CommentCreated(UpdateCommentCountDto dto, string postType);
        Guid BookmarkCreated(BookmarkChangedDto dto, string postType);
    }
}
