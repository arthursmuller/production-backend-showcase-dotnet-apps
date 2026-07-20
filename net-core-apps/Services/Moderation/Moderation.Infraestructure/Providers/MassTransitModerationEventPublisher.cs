using MassTransit;
using Moderation.Application.Providers;
using Moderation.Contracts;
using Moderation.Contracts.Restricted;
using MessageBrokerContracts.Privacy.Restricted;
using MessageBrokerExtensions;
using Moderation.Infra.Database.Ef;

namespace Moderation.Infra.Providers;

public sealed class MassTransitModerationEventPublisher(
    IPublishEndpoint endpoint,
    ModerationDbContext context)
    : IModerationEventPublisher
{
    public Task PublishDecisionAsync(
        ModerationDecisionMade message,
        CancellationToken cancellationToken) =>
        PublishStagedAsync(
            message,
            ModerationMessageIdentity.Create("decision", message.CaseId, message.DecisionVersion),
            cancellationToken);

    public Task PublishActionAsync(
        ModerationActionApplied message,
        CancellationToken cancellationToken) =>
        PublishStagedAsync(
            message,
            ModerationMessageIdentity.Create("action", message.CaseId, message.DecisionVersion),
            cancellationToken);

    public Task PublishEscalationAsync(
        ModerationCaseEscalatedRestricted message,
        CancellationToken cancellationToken) =>
        PublishStagedAsync(
            message,
            ModerationMessageIdentity.Create("escalation", message.CaseId, message.CaseVersion),
            cancellationToken);

    public Task PublishCaseChangedAsync(
        ModerationCaseChangedRestricted message,
        CancellationToken cancellationToken) =>
        PublishStagedAsync(
            message,
            ModerationMessageIdentity.Create("case-changed", message.CaseId, message.CaseVersion),
            cancellationToken);

    public Task PublishErasureCompletedAsync(
        UserErasureCompleted message,
        CancellationToken cancellationToken) =>
        PublishStagedAsync(message, message.EventId, cancellationToken);

    private async Task PublishStagedAsync<TMessage>(
        TMessage message,
        Guid messageId,
        CancellationToken cancellationToken)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);
        await context.PublishToBusOutboxAsync(
            endpoint,
            message,
            messageId,
            cancellationToken);
    }
}
