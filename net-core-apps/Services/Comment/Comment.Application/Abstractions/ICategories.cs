namespace Comment.Application.Abstractions
{
    public interface ICategories
    {
        void UpdateMentions(string[] namesToAdd, string[] namesToRemove);
    }
}
