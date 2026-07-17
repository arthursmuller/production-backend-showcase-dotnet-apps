namespace Comment.Application.Abstractions
{
    public sealed record CurrentUser(int Id, string Name);

    public interface IIdentity
    {
        CurrentUser User { get; }
    }
}
