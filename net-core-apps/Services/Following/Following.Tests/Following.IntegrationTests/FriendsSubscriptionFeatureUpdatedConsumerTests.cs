using Following.API.AppInterfaces.Queues;
using Following.Application.Dtos;
using Following.Application.Features.Subscriptions;
using Following.Application.Providers.Relationships;
using Following.Application.Providers.Time;
using Following.Domain.AggregatesModel;
using Following.Domain.Repositories;
using MassTransit;
using MessageBrokerContracts.Following;
using Xunit;

namespace Following.UnitTests;

public sealed class FriendsSubscriptionFeatureUpdatedConsumerTests
{
    [Fact]
    public void Map_preserves_the_canonical_subscription_feature_payload()
    {
        var endDate = new DateTime(2026, 8, 1, 12, 30, 0, DateTimeKind.Utc);
        var contract = new SubscriptionFeatureUpdated
        {
            EventId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow,
            Owner = new SubscriptionFeatureParticipant
            {
                UserId = 101,
                DisplayName = "owner"
            },
            Requester = new SubscriptionFeatureParticipant
            {
                UserId = 202,
                DisplayName = "requester"
            },
            SubscriptionId = "subscription-42",
            EndDateUtc = endDate
        };

        var dto = FriendsSubscriptionFeatureUpdatedConsumer.Map(contract);

        Assert.Equal(101, dto.Owner.Id);
        Assert.Equal("owner", dto.Owner.Name);
        Assert.Equal(202, dto.Requester.Id);
        Assert.Equal("requester", dto.Requester.Name);
        Assert.Equal("subscription-42", dto.SubscriptionId);
        Assert.Equal(endDate, dto.EndDate);
    }

    [Fact]
    public void Consumer_and_definition_use_the_typed_MassTransit_shape()
    {
        Assert.True(typeof(IConsumer<SubscriptionFeatureUpdated>)
            .IsAssignableFrom(typeof(FriendsSubscriptionFeatureUpdatedConsumer)));
        Assert.True(typeof(ConsumerDefinition<FriendsSubscriptionFeatureUpdatedConsumer>)
            .IsAssignableFrom(typeof(FriendsSubscriptionFeatureUpdatedConsumerDefinition)));
    }

    [Fact]
    public void Map_rejects_a_non_utc_timestamp()
    {
        var contract = new SubscriptionFeatureUpdated
        {
            EventId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            Owner = new SubscriptionFeatureParticipant
            {
                UserId = 101,
                DisplayName = "owner",
            },
            Requester = new SubscriptionFeatureParticipant
            {
                UserId = 202,
                DisplayName = "requester",
            },
            SubscriptionId = "subscription-42",
        };

        Assert.Throws<InvalidOperationException>(() =>
            FriendsSubscriptionFeatureUpdatedConsumer.Map(contract));
    }

    [Theory]
    [InlineData(true, 0, 0)]
    [InlineData(false, 2, 2)]
    public async Task Subscription_friendship_creation_is_block_aware_and_emits_both_directions(
        bool blocked,
        int expectedFriendships,
        int expectedEvents)
    {
        var repository = new SubscriptionRepository(blocked);
        var events = new CapturingRelationshipEvents();
        var command = new SetFriendsSubscriptionFeatureCommand(
            repository,
            new FixedClock(new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc)),
            events);
        var input = new SubscriptionFeatureUpdatedDto(
            new SubscriptionFeatureUpdatedUserDto(10, "owner"),
            new SubscriptionFeatureUpdatedUserDto(20, "requester"),
            "subscription-privacy-test",
            new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc));

        var result = await command.Handle(input, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(repository.Features);
        Assert.Equal(expectedFriendships, repository.Friendships.Count);
        Assert.Equal(expectedEvents, events.Transitions.Count);
        Assert.All(events.Transitions, transition =>
        {
            Assert.Equal(RelationshipKinds.Friend, transition.RelationshipKind);
            Assert.Equal(RelationshipStates.Active, transition.State);
        });
        Assert.Equal(1, repository.SaveCount);
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class CapturingRelationshipEvents : IRelationshipEventWriter
    {
        public List<RelationshipTransition> Transitions { get; } = [];

        public Task<RelationshipStateChanged> StageAsync(
            RelationshipTransition transition,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Transitions.Add(transition);
            return Task.FromResult(new RelationshipStateChanged
            {
                EventId = Guid.NewGuid(),
                StreamId = Guid.NewGuid(),
                StreamVersion = 1,
                OwnerUserId = transition.OwnerUserId,
                ViewerUserId = transition.ViewerUserId,
                RelationshipKind = transition.RelationshipKind,
                State = transition.State,
                OccurredAtUtc = DateTime.UtcNow,
                ReasonCode = transition.ReasonCode,
            });
        }
    }

    private sealed class SubscriptionRepository(bool blocked) : IFollowingRepository
    {
        public List<FriendshipSubscriptionFeature> Features { get; } = [];
        public List<Friendship> Friendships { get; } = [];
        public Dictionary<int, UserRelationAnalytics> Analytics { get; } = [];
        public int SaveCount { get; private set; }

        public Task<IRelationshipPairMutation> AcquireRelationshipPairMutation(
            int firstUserId,
            int secondUserId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IRelationshipPairMutation>(
                NoopRelationshipPairMutation.Instance);
        }

        public Task<IUserMutationScope> AcquireUserMutation(
            int userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IUserMutationScope>(NoopUserMutationScope.Instance);

        public Task<IUserMutationScope> AcquireUserErasureMutation(
            int userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IUserMutationScope>(NoopUserMutationScope.Instance);

        public Task EnsureUsersNotErased(
            IReadOnlyCollection<int> userIds,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> IsBlockedEitherDirection(
            int firstUserId,
            int secondUserId,
            CancellationToken cancellationToken) => Task.FromResult(blocked);

        public Task<FriendshipSubscriptionFeature?> GetSubscriptionFeature(
            string subscriptionId,
            int requesterId,
            int ownerId,
            CancellationToken cancellationToken) => Task.FromResult<FriendshipSubscriptionFeature?>(null);

        public Task<bool> IsFriend(
            int userId,
            int friendId,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<UserRelationAnalytics> GetOrCreateAnalytics(
            int userId,
            int shardDiscriminator,
            CancellationToken cancellationToken)
        {
            if (!Analytics.TryGetValue(userId, out var analytics))
            {
                analytics = new UserRelationAnalytics(userId);
                Analytics.Add(userId, analytics);
            }

            return Task.FromResult(analytics);
        }

        public Task Add<T>(T entity, CancellationToken cancellationToken) where T : class
        {
            if (entity is FriendshipSubscriptionFeature feature)
                Features.Add(feature);
            return Task.CompletedTask;
        }

        public Task AddRange<T>(IEnumerable<T> entities, CancellationToken cancellationToken)
            where T : class
        {
            Friendships.AddRange(entities.OfType<Friendship>());
            return Task.CompletedTask;
        }

        public void Remove<T>(T entity) where T : class { }
        public void RemoveRange<T>(IEnumerable<T> entities) where T : class { }

        public Task SaveChanges(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task<UserBlock?> GetBlock(int blockerUserId, int blockedUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CategoryFollow?> GetCategoryFollow(int userId, int? categoryId, string? categoryName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Follow?> GetFollow(int userId, int followedId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FriendRequest?> GetFriendRequest(int userId, int friendId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FriendRequest?> GetFriendRequestForApproval(int requestId, int friendId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Friendship>> GetFriendships(int userId, int friendId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
