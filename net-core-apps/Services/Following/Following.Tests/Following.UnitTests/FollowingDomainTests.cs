using Following.Domain.AggregatesModel;
using Following.Domain.Dtos;
using Following.Domain.Exceptions;
using StarterEntities;
using Xunit;

namespace Following.UnitTests;

public sealed class FollowingDomainTests
{
    [Fact]
    public void Reciprocal_follows_are_linked_in_both_directions()
    {
        var firstUser = new User(7, "First User");
        var secondUser = new User(11, "Second User");
        var outgoing = Follow.Create(firstUser, secondUser, existing: null);
        var incoming = Follow.Create(secondUser, firstUser, existing: null);

        outgoing.LinkReciprocal(incoming);

        Assert.True(outgoing.IsFollowedBack);
        Assert.True(incoming.IsFollowedBack);
    }

    [Fact]
    public void User_cannot_follow_themselves()
    {
        var user = new User(7, "Same User");

        var exception = Assert.Throws<FollowingException>(
            () => Follow.Create(user, user, existing: null));

        Assert.Equal("A user cannot create a relationship with themselves.", exception.Message);
    }

    [Fact]
    public void Analytics_counters_never_become_negative()
    {
        var analytics = new UserRelationAnalytics(7);

        analytics.DecrementFollowers();
        analytics.DecrementFriends();
        analytics.DecrementFollowing();

        Assert.Equal(0, analytics.TotalFollowers);
        Assert.Equal(0, analytics.TotalFriends);
        Assert.Equal(0, analytics.TotalFollowing);
    }

    [Fact]
    public void Subscription_must_end_in_the_future()
    {
        var now = new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

        Assert.Throws<FollowingException>(() => FriendshipSubscriptionFeature.Create(
            ownerId: 7,
            requesterId: 11,
            subscriptionId: "subscription-42",
            new SubscriptionFeatureUpdateDomainDto { EndDate = now },
            now));
    }
}
