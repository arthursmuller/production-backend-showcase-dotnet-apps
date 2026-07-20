using Feed.API.AppInterfaces.Queues;
using Moderation.Contracts;
using Xunit;

namespace Feed.IntegrationTests;

public sealed class FeedModerationConsumerMappingTests
{
    [Fact]
    public void Decision_and_action_events_map_to_identical_feed_coordinates()
    {
        var caseId = Guid.Parse("2a21639f-76aa-45d4-a365-8bc055111ac3");
        var occurredAtUtc = new DateTimeOffset(
            2026, 7, 22, 14, 5, 6, TimeSpan.Zero);

        foreach (var resourceKind in Enum.GetValues<ModeratedResourceKind>())
            foreach (var action in Enum.GetValues<ModerationDecisionAction>())
            {
                var decision = ModerationDecisionMadeConsumer.Map(
                    new ModerationDecisionMade(
                        caseId,
                        resourceKind,
                        "resource-42",
                        action,
                        17,
                        occurredAtUtc));
                var applied = ModerationActionAppliedConsumer.Map(
                    new ModerationActionApplied(
                        caseId,
                        resourceKind,
                        "resource-42",
                        action,
                        17,
                        occurredAtUtc));

                Assert.Equal(decision, applied);
            }
    }
}
