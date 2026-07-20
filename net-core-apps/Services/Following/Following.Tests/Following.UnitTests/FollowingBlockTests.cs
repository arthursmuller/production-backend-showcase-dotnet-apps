using Following.Application.Features.Followings;
using Following.Application.Providers.Relationships;
using Following.Domain.AggregatesModel;
using Following.Domain.Dtos;
using Following.Domain.Repositories;
using IdentityProvider;
using MessageBrokerContracts.Following;
using StarterEntities;
using Xunit;

namespace Following.UnitTests;

public sealed class FollowingBlockTests
{
    [Fact]
    public async Task Block_revokes_every_access_relationship_and_is_idempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = new StubRepository();
        repository.Follows.Add(Follow.Create(new User(10, "one"), new User(20, "two"), null));
        repository.Follows.Add(Follow.Create(new User(20, "two"), new User(10, "one"), null));
        repository.Friendships.AddRange(
            Friendship.CreatePair(new User(10, "one"), new User(20, "two"), false));
        repository.Requests.Add(FriendRequest.Create(
            new User(10, "one"),
            new User(20, "two"),
            existingRequest: null,
            alreadyFriends: false));
        var firstAnalytics = await repository.GetOrCreateAnalytics(10, 20, cancellationToken);
        var secondAnalytics = await repository.GetOrCreateAnalytics(20, 10, cancellationToken);
        firstAnalytics.IncrementFollowing();
        firstAnalytics.IncrementFollowers();
        firstAnalytics.IncrementFriends();
        secondAnalytics.IncrementFollowing();
        secondAnalytics.IncrementFollowers();
        secondAnalytics.IncrementFriends();
        secondAnalytics.IncrementFriendRequests();

        var events = new StubRelationshipEvents();
        var command = new BlockUserCommand(repository, new StubIdentity(10), events);

        Assert.True(await command.Handle(20, cancellationToken));
        Assert.False(await command.Handle(20, cancellationToken));

        Assert.Empty(repository.Follows);
        Assert.Empty(repository.Friendships);
        Assert.Empty(repository.Requests);
        Assert.Single(repository.Blocks);
        Assert.Equal(0, firstAnalytics.TotalFollowing);
        Assert.Equal(0, firstAnalytics.TotalFollowers);
        Assert.Equal(0, firstAnalytics.TotalFriends);
        Assert.Equal(0, secondAnalytics.TotalFollowing);
        Assert.Equal(0, secondAnalytics.TotalFollowers);
        Assert.Equal(0, secondAnalytics.TotalFriends);
        Assert.Equal(0, secondAnalytics.TotalFriendsRequests);
        Assert.Equal(5, events.Transitions.Count);
        Assert.Equal(2, events.Transitions.Count(item =>
            item.RelationshipKind == RelationshipKinds.Follow
            && item.State == RelationshipStates.Revoked));
        Assert.Equal(2, events.Transitions.Count(item =>
            item.RelationshipKind == RelationshipKinds.Friend
            && item.State == RelationshipStates.Revoked));
        Assert.Single(events.Transitions, item =>
            item.RelationshipKind == RelationshipKinds.Block
            && item.State == RelationshipStates.Blocked);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Unblock_revokes_only_the_directional_block_and_does_not_restore_relationships()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = new StubRepository();
        repository.Blocks.Add(UserBlock.Create(10, 20, existing: null));
        var events = new StubRelationshipEvents();
        var command = new UnblockUserCommand(repository, new StubIdentity(10), events);

        Assert.True(await command.Handle(20, cancellationToken));
        Assert.False(await command.Handle(20, cancellationToken));

        Assert.Empty(repository.Blocks);
        var transition = Assert.Single(events.Transitions);
        Assert.Equal(10, transition.OwnerUserId);
        Assert.Equal(20, transition.ViewerUserId);
        Assert.Equal(RelationshipKinds.Block, transition.RelationshipKind);
        Assert.Equal(RelationshipStates.Revoked, transition.State);
        Assert.Empty(repository.Follows);
        Assert.Empty(repository.Friendships);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Block_emits_explicit_bidirectional_revocations_when_authority_rows_are_absent()
    {
        var events = new StubRelationshipEvents();
        var repository = new StubRepository();
        var command = new BlockUserCommand(repository, new StubIdentity(10), events);

        Assert.True(await command.Handle(20, TestContext.Current.CancellationToken));

        Assert.Equal(5, events.Transitions.Count);
        Assert.Equal(
            [(10, 20), (20, 10)],
            events.Transitions
                .Where(item => item.RelationshipKind == RelationshipKinds.Friend)
                .Select(item => (item.OwnerUserId, item.ViewerUserId))
                .Order()
                .ToArray());
        Assert.Equal(
            [(10, 20), (20, 10)],
            events.Transitions
                .Where(item => item.RelationshipKind == RelationshipKinds.Follow)
                .Select(item => (item.OwnerUserId, item.ViewerUserId))
                .Order()
                .ToArray());
        Assert.Single(events.Transitions, item =>
            item.RelationshipKind == RelationshipKinds.Block
            && item.State == RelationshipStates.Blocked);
    }

    [Fact]
    public void User_block_rejects_self_relationships()
    {
        Assert.Throws<Following.Domain.Exceptions.FollowingException>(
            () => UserBlock.Create(10, 10, existing: null));
    }

    [Fact]
    public async Task Tombstoned_participant_rejects_mutation_before_any_state_or_event_change()
    {
        var repository = new StubRepository();
        repository.ErasedUsers.Add(20);
        var events = new StubRelationshipEvents();
        var command = new BlockUserCommand(repository, new StubIdentity(10), events);

        var exception = await Assert.ThrowsAsync<Following.Domain.Exceptions.FollowingException>(
            () => command.Handle(20, TestContext.Current.CancellationToken));

        Assert.Equal(410, exception.StatusCode);
        Assert.Empty(repository.Blocks);
        Assert.Empty(events.Transitions);
        Assert.Equal(0, repository.SaveCount);
    }

    private sealed class StubIdentity(int userId) : IIdentityService
    {
        public AuthUser User { get; } = new(userId, $"user-{userId}");
    }

    private sealed class StubRelationshipEvents : IRelationshipEventWriter
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

    private sealed class StubRepository : IFollowingRepository
    {
        public List<UserBlock> Blocks { get; } = [];
        public List<Follow> Follows { get; } = [];
        public List<Friendship> Friendships { get; } = [];
        public List<FriendRequest> Requests { get; } = [];
        public Dictionary<int, UserRelationAnalytics> Analytics { get; } = [];
        public HashSet<int> ErasedUsers { get; } = [];
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
            CancellationToken cancellationToken)
        {
            if (userIds.Any(ErasedUsers.Contains))
            {
                throw new Following.Domain.Exceptions.FollowingException(
                    "A relationship participant is no longer available.",
                    410);
            }
            return Task.CompletedTask;
        }

        public Task<UserBlock?> GetBlock(
            int blockerUserId,
            int blockedUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Blocks.SingleOrDefault(item =>
                item.BlockerUserId == blockerUserId
                && item.BlockedUserId == blockedUserId));

        public Task<bool> IsBlockedEitherDirection(
            int firstUserId,
            int secondUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Blocks.Any(item =>
                (item.BlockerUserId == firstUserId && item.BlockedUserId == secondUserId)
                || (item.BlockerUserId == secondUserId && item.BlockedUserId == firstUserId)));

        public Task<Follow?> GetFollow(
            int userId,
            int followedId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Follows.SingleOrDefault(item =>
                item.FollowerUserId == userId && item.FollowedUserId == followedId));

        public Task<IReadOnlyList<Friendship>> GetFriendships(
            int userId,
            int friendId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Friendship>>(Friendships.Where(item =>
                (item.UserId == userId && item.FriendUserId == friendId)
                || (item.UserId == friendId && item.FriendUserId == userId)).ToArray());

        public Task<FriendRequest?> GetFriendRequest(
            int userId,
            int friendId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Requests.SingleOrDefault(item =>
                (item.SenderUserId == userId && item.RecipientUserId == friendId)
                || (item.SenderUserId == friendId && item.RecipientUserId == userId)));

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
            if (entity is UserBlock block)
                Blocks.Add(block);
            return Task.CompletedTask;
        }

        public Task AddRange<T>(IEnumerable<T> entities, CancellationToken cancellationToken)
            where T : class => Task.CompletedTask;

        public void Remove<T>(T entity) where T : class
        {
            switch (entity)
            {
                case UserBlock block:
                    Blocks.Remove(block);
                    break;
                case Follow follow:
                    Follows.Remove(follow);
                    break;
                case FriendRequest request:
                    Requests.Remove(request);
                    break;
            }
        }

        public void RemoveRange<T>(IEnumerable<T> entities) where T : class
        {
            foreach (var entity in entities.ToArray())
            {
                if (entity is Friendship friendship)
                    Friendships.Remove(friendship);
            }
        }

        public Task SaveChanges(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task<CategoryFollow?> GetCategoryFollow(int userId, int? categoryId, string? categoryName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FriendRequest?> GetFriendRequestForApproval(int requestId, int friendId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsFriend(int userId, int friendId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FriendshipSubscriptionFeature?> GetSubscriptionFeature(string subscriptionId, int requesterId, int ownerId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
