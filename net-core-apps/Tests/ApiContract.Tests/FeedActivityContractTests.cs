using MessageBrokerContracts.Feed;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ApiContract.Tests;

public sealed class FeedActivityContractTests
{
    [Fact]
    public void Feed_activity_has_stable_canonical_wire_fields()
    {
        var message = new FeedActivityChanged
        {
            EventId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            StreamId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            StreamVersion = 7,
            TargetType = "comment",
            TargetId = "comment-42",
            ActivityKind = "created",
            ActorUserId = 9,
            AudiencePolicy = FeedAudiencePolicies.Followers,
            State = FeedActivityStates.Active,
            RankKey = 638883236960000000,
            PublishedAtUtc = new DateTime(2026, 7, 17, 12, 34, 56, DateTimeKind.Utc),
            OccurredAtUtc = new DateTime(2026, 7, 17, 12, 35, 0, DateTimeKind.Utc),
            ReasonCode = "comment.created",
        };

        var json = JsonNode.Parse(JsonSerializer.Serialize(message))!.AsObject();
        Assert.Null(json["SchemaVersion"]);
        Assert.Equal(message.EventId, json["EventId"]!.GetValue<Guid>());
        Assert.Equal(message.StreamId, json["StreamId"]!.GetValue<Guid>());
        Assert.Equal(7, json["StreamVersion"]!.GetValue<long>());
        Assert.Equal("comment", json["TargetType"]!.GetValue<string>());
        Assert.Equal("comment-42", json["TargetId"]!.GetValue<string>());
        Assert.Equal("created", json["ActivityKind"]!.GetValue<string>());
        Assert.Equal("followers", json["AudiencePolicy"]!.GetValue<string>());
        Assert.Equal("active", json["State"]!.GetValue<string>());
        Assert.Null(json["CreationDate"]);
        Assert.Null(json["Dto"]);

        Assert.Equal(message, json.Deserialize<FeedActivityChanged>());
    }

    [Fact]
    public void Feed_wire_enums_are_closed_and_lowercase()
    {
        Assert.All(
            new[]
            {
                FeedActivityStates.Active,
                FeedActivityStates.Revoked,
                FeedActivityStates.Deleted,
                FeedAudiencePolicies.Public,
                FeedAudiencePolicies.Followers,
                FeedAudiencePolicies.Friends,
                FeedAudiencePolicies.Owner,
            },
            value => Assert.Equal(value.ToLowerInvariant(), value));
        Assert.False(FeedActivityStates.IsSupported("Active"));
        Assert.False(FeedAudiencePolicies.IsSupported("global"));
    }
}
