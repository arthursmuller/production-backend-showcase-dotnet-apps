namespace Comment.Application.Abstractions
{
    public interface IPosts
    {
        void CommentCreated(
            string relatedEntityId,
            bool increment,
            int userId,
            string postType);
    }
}
