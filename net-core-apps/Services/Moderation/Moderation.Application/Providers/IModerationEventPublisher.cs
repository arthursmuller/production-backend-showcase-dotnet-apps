using Moderation.Contracts;
using Moderation.Contracts.Restricted;
using MessageBrokerContracts.Privacy.Restricted;

namespace Moderation.Application.Providers;

public interface IModerationEventPublisher
{
    Task PublishDecisionAsync(ModerationDecisionMade message, CancellationToken cancellationToken);
    Task PublishActionAsync(ModerationActionApplied message, CancellationToken cancellationToken);
    Task PublishEscalationAsync(
        ModerationCaseEscalatedRestricted message,
        CancellationToken cancellationToken);
    Task PublishCaseChangedAsync(
        ModerationCaseChangedRestricted message,
        CancellationToken cancellationToken);
    Task PublishErasureCompletedAsync(
        UserErasureCompleted message,
        CancellationToken cancellationToken);
}
