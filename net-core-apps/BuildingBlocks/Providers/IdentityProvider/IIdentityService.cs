namespace IdentityProvider
{
    public record AuthUser(int Id, string Name);

    public interface IIdentityService
    {
        AuthUser User { get; }
    }
}
