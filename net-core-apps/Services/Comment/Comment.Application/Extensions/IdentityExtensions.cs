using Comment.Application.Abstractions;
using StarterEntities;

namespace Comment.Application.Extensions
{
    public static class IdentityExtensions
    {
        public static User ToDomainUser(this CurrentUser user) =>
            user is null ? null : new User(user.Id, user.Name);
    }
}
