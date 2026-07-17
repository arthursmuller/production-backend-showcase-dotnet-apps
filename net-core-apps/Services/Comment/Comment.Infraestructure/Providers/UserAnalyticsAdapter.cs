using Comment.Application.Abstractions;
using UserProvider.Abstractions;
using UserProvider.Dtos;

namespace Comment.Infra.Providers
{
    public sealed class UserAnalyticsAdapter(IUserService users) : IUserAnalytics
    {
        public void UpdateLikes(int userId, bool increment) =>
            users.Broker.UpdateAnalytics(new UserUpdateAnalytics
            {
                UserId = userId,
                IncrementLikes = increment,
            });

        public void UpdateViews(int userId, bool increment) =>
            users.Broker.UpdateAnalytics(new UserUpdateAnalytics
            {
                UserId = userId,
                IncrementViews = increment,
            });
    }
}
