namespace Comment.Application.Abstractions
{
    public interface IFeed
    {
        void Remove(string entityType, string entityId);
    }
}
