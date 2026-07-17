using Comment.Application.Abstractions;
using IdentityProvider;

namespace Comment.Infra.Providers
{
    public sealed class IdentityAdapter(IIdentityService identity) : IIdentity
    {
        public CurrentUser User => identity.User is null
            ? null
            : new CurrentUser(identity.User.Id, identity.User.Name);
    }
}
