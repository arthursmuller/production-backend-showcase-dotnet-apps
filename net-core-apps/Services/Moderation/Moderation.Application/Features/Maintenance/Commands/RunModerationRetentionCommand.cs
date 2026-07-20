using Microsoft.Extensions.Options;
using Moderation.Application.Options;
using Moderation.Application.Telemetry;
using Moderation.Domain.Repositories;

namespace Moderation.Application.Features.Maintenance.Commands;

public sealed class RunModerationRetentionCommand(
    IModerationRetentionStore store,
    IOptions<ModerationRetentionOptions> options,
    TimeProvider timeProvider)
{
    public async Task<ModerationRetentionBatchResult> ExecuteAsync(
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            return new ModerationRetentionBatchResult(false, 0, 0, 0);

        var now = timeProvider.GetUtcNow();
        var result = await store.ExecuteBatchAsync(
            now.AddDays(-settings.ReportsAndCasesDays),
            now.AddDays(-settings.AuditDays),
            now,
            leaseOwner,
            TimeSpan.FromSeconds(settings.LeaseSeconds),
            settings.MaxBatchSize,
            cancellationToken);

        if (result.CasesRedacted > 0)
            ModerationTelemetry.RetentionRedacted.Add(result.CasesRedacted);
        if (result.CasesPurged > 0)
            ModerationTelemetry.RetentionPurged.Add(result.CasesPurged);
        return result;
    }
}
