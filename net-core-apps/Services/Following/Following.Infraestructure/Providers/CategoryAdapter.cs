using CategoriesProvider.Abstractions;
using Following.Application.Providers.Categories;
using Following.Infra.Persistence;
using MessageBrokerContracts.Categories;
using MessageBrokerCore.Abstractions;
using MessageBrokerExtensions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Following.Infra.Providers
{
    public sealed class CategoryAdapter(
        ICategoriesApi categories,
        IIntegrationMessagePublisher publisher,
        FollowingContext context,
        TimeProvider timeProvider) : ICategories
    {
        public Task<int> GetIdByName(string name, CancellationToken cancellationToken) =>
            categories.GetIdByName(name, cancellationToken);

        public async Task UpdateFollowAsync(
            int[] categoryIds,
            string[] categoryNames,
            bool isFollowing,
            CancellationToken cancellationToken)
        {
            var eventId = Guid.NewGuid();
            await context.PublishToBusOutboxAsync(
                publisher,
                new CategoryFollowsChanged
                {
                    EventId = eventId,
                    OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime,
                    CategoryIds = categoryIds ?? [],
                    CategoryNames = categoryNames ?? [],
                    IsFollowing = isFollowing,
                },
                eventId,
                cancellationToken);
        }
    }
}
