using MessageBrokerContracts.Following;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ApiContract.Tests;

public sealed class RelationshipContractTests
{
    [Fact]
    public void Relationship_event_has_stable_canonical_directional_wire_fields()
    {
        var message = new RelationshipStateChanged
        {
            EventId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            StreamId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            StreamVersion = 42,
            OwnerUserId = 7,
            ViewerUserId = 8,
            RelationshipKind = RelationshipKinds.Block,
            State = RelationshipStates.Blocked,
            OccurredAtUtc = new DateTime(2026, 7, 17, 12, 34, 56, DateTimeKind.Utc),
            ReasonCode = "user.blocked",
        };

        var json = JsonNode.Parse(JsonSerializer.Serialize(message))!.AsObject();
        Assert.Null(json["SchemaVersion"]);
        Assert.Equal(message.EventId, json["EventId"]!.GetValue<Guid>());
        Assert.Equal(message.StreamId, json["StreamId"]!.GetValue<Guid>());
        Assert.Equal(42, json["StreamVersion"]!.GetValue<long>());
        Assert.Equal(7, json["OwnerUserId"]!.GetValue<int>());
        Assert.Equal(8, json["ViewerUserId"]!.GetValue<int>());
        Assert.Equal("block", json["RelationshipKind"]!.GetValue<string>());
        Assert.Equal("blocked", json["State"]!.GetValue<string>());
        Assert.Equal("user.blocked", json["ReasonCode"]!.GetValue<string>());
        Assert.Null(json["Id"]);
        Assert.Null(json["CreationDate"]);

        var roundTrip = json.Deserialize<RelationshipStateChanged>();
        Assert.Equal(message, roundTrip);
    }

    [Fact]
    public void Relationship_snapshot_carries_the_durable_stream_checkpoint()
    {
        var snapshot = new RelationshipStateSnapshot
        {
            Cursor = 91,
            StreamId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            LastEventId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            StreamVersion = 9,
            OwnerUserId = 10,
            ViewerUserId = 11,
            RelationshipKind = RelationshipKinds.Friend,
            State = RelationshipStates.Active,
            AsOfUtc = new DateTime(2026, 7, 17, 13, 0, 0, DateTimeKind.Utc),
        };

        var json = JsonNode.Parse(JsonSerializer.Serialize(snapshot))!.AsObject();
        Assert.Null(json["SchemaVersion"]);
        Assert.Equal(91, json["Cursor"]!.GetValue<long>());
        Assert.Equal(9, json["StreamVersion"]!.GetValue<long>());
        Assert.Equal(snapshot.LastEventId, json["LastEventId"]!.GetValue<Guid>());
        Assert.True(RelationshipKinds.IsSupported(json["RelationshipKind"]!.GetValue<string>()));
        Assert.True(RelationshipStates.IsSupported(json["State"]!.GetValue<string>()));
    }
}
