using MessageBrokerContracts.Privacy.Restricted;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ApiContract.Tests;

public sealed class PrivacyContractTests
{
    [Fact]
    public void Restricted_user_erasure_contract_has_a_stable_minimal_round_trip()
    {
        var request = new UserErasureRequested
        {
            EventId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            RequestId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            UserId = 42,
            ErasureVersion = 7,
            RequestedAtUtc = new DateTimeOffset(2026, 7, 17, 12, 34, 56, TimeSpan.Zero),
        };

        var json = JsonNode.Parse(JsonSerializer.Serialize(request))!.AsObject();
        var roundTrip = json.Deserialize<UserErasureRequested>();

        Assert.NotNull(roundTrip);
        Assert.Equal(request, roundTrip);
        Assert.Null(json["SchemaVersion"]);
        Assert.Equal(
            [
                nameof(UserErasureRequested.ErasureVersion),
                nameof(UserErasureRequested.EventId),
                nameof(UserErasureRequested.RequestId),
                nameof(UserErasureRequested.RequestedAtUtc),
                nameof(UserErasureRequested.UserId),
            ],
            json.Select(property => property.Key).Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(json, property =>
            property.Key.Contains("Reason", StringComparison.OrdinalIgnoreCase)
            || property.Key.Contains("Email", StringComparison.OrdinalIgnoreCase)
            || property.Key.Contains("Content", StringComparison.OrdinalIgnoreCase)
            || property.Key.Contains("Evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Restricted_completion_contract_carries_no_deleted_data_or_failure_detail()
    {
        var properties = typeof(UserErasureCompleted)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Contains(nameof(UserErasureCompleted.RequestId), properties);
        Assert.Contains(nameof(UserErasureCompleted.ErasureVersion), properties);
        Assert.Contains(nameof(UserErasureCompleted.Service), properties);
        Assert.DoesNotContain(properties, property =>
            property.Contains("Count", StringComparison.OrdinalIgnoreCase)
            || property.Contains("Error", StringComparison.OrdinalIgnoreCase)
            || property.Contains("Detail", StringComparison.OrdinalIgnoreCase)
            || property.Contains("Evidence", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("completed", UserErasureOutcomes.Completed);
        Assert.Equal("superseded", UserErasureOutcomes.Superseded);
    }
}
