namespace IdentityProvider
{
    public record AuthUser(int Id, string Name, string Cpf, bool? IsVerified, bool IsAdmin, string Tenant);

    public interface IIdentityService
    {
        AuthUser User { get; set; }
        string ReqIpAddress { get; set; }
        string AccessToken { get; set; }
        string ReqTimeZone { get; set; }
    }
}
