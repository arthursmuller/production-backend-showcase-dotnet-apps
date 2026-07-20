using Microsoft.Extensions.Options;
using Moderation.Application.Options;
using Moderation.Application.Providers;
using Moderation.Application.Telemetry;
using Moderation.Contracts.Restricted;
using Moderation.Domain.Repositories;

namespace Moderation.Application.Features.Cases.Commands;

public sealed class EscalateOverdueCasesCommand(
    IModerationRepository repository,
    IModerationEventPublisher publisher,
    TimeProvider timeProvider,
    IOptions<ModerationEscalationOptions> escalationOptions)
{
    private readonly ModerationEscalationOptions _escalation = escalationOptions.Value;

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        using var activity = ModerationTelemetry.ActivitySource.StartActivity("moderation.sla.escalate");
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow();
        var cases = await repository.GetOverdueCasesAsync(
            now,
            _escalation.MaxBatchSize,
            cancellationToken);
        var changed = 0;
        foreach (var moderationCase in cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!moderationCase.EscalateIfOverdue("moderation-sla-worker", now))
                continue;
            await publisher.PublishEscalationAsync(
                new ModerationCaseEscalatedRestricted(
                    moderationCase.Id,
                    moderationCase.Priority.ToString(),
                    moderationCase.Version,
                    now),
                cancellationToken);
            changed++;
        }

        if (changed > 0)
        {
            await repository.SaveChangesAsync(cancellationToken);
            ModerationTelemetry.Escalations.Add(changed);
        }
        activity?.SetTag("moderation.escalated.count", changed);
        return changed;
    }
}
