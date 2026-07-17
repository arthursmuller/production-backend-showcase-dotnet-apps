using Following.Application.Abstractions;
using IdentityProvider;
using StarterEntities;

namespace Following.Infra.Providers
{
    public sealed class IdentityAdapter(IIdentityService identity) : IIdentity
    {
        public User User => identity.User is null
            ? null
            : new User(identity.User.Id, identity.User.Name);
    }
}
