using System;
using IdentityProvider;
using StarterEntities;

namespace Comment.Application.Extensions
{
    public static class IdentityExtensions
    {
        public static User ToDomainUser(this AuthUser user)
        {
            ArgumentNullException.ThrowIfNull(user);
            return new User(user.Id, user.Name);
        }
    }
}
