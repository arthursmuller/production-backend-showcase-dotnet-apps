using Comment.Application.Abstractions;
using Posts.Provider.Abstractions;
using StarterEntities;

namespace Comment.Infra.Providers
{
    public sealed class PostsAdapter(IPostsService posts) : IPosts
    {
        public void CommentCreated(
            string relatedEntityId,
            bool increment,
            int userId,
            string postType) =>
            posts.Broker.CommentCreated(
                new UpdateCommentCountDto(relatedEntityId, increment, userId),
                postType);
    }
}
