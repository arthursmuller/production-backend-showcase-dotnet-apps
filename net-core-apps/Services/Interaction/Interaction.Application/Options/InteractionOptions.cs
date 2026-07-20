using Interaction.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace Interaction.Application.Options;

public sealed class InteractionOptions
{
    public const string SectionName = "Interaction";
    public int StatusBatchMaximum { get; set; } = 100;
    public int ViewDedupeBucketSeconds { get; set; } = 900;
    public int ViewObservationRetentionDays { get; set; } = 7;
    public int ViewAggregationBatchSize { get; set; } = 500;
    public int ViewAggregationPollSeconds { get; set; } = 2;
    public int ReconciliationBatchSize { get; set; } = 100;
    public int ReconciliationPollSeconds { get; set; } = 300;
    public int AuthorityRevisionRetentionDays { get; set; } = 7;
    public int AuthorityRevisionPruneBatchSize { get; set; } = 500;
    public int ProjectionGapDrainMaximum { get; set; } = 64;
    public int ErasureBatchSize { get; set; } = 100;
    public int ErasurePollSeconds { get; set; } = 5;
    public int ErasureLeaseSeconds { get; set; } = 120;
}

public sealed class InteractionOptionsValidator : IValidateOptions<InteractionOptions>
{
    public ValidateOptionsResult Validate(string? name, InteractionOptions value)
    {
        var failures = new List<string>();
        if (value.StatusBatchMaximum is < 1 or > 250)
            failures.Add("Interaction:StatusBatchMaximum must be between 1 and 250.");
        if (value.ViewDedupeBucketSeconds is < 60 or > 86400)
            failures.Add("Interaction:ViewDedupeBucketSeconds must be between 60 and 86400.");
        if (value.ViewObservationRetentionDays is < 1 or > 30)
            failures.Add("Interaction:ViewObservationRetentionDays must be between 1 and 30.");
        if (value.ViewAggregationBatchSize is < 1 or > 5000)
            failures.Add("Interaction:ViewAggregationBatchSize must be between 1 and 5000.");
        if (value.ViewAggregationPollSeconds is < 1 or > 60)
            failures.Add("Interaction:ViewAggregationPollSeconds must be between 1 and 60.");
        if (value.ReconciliationBatchSize is < 1 or > 1000)
            failures.Add("Interaction:ReconciliationBatchSize must be between 1 and 1000.");
        if (value.ReconciliationPollSeconds is < 30 or > 86400)
            failures.Add("Interaction:ReconciliationPollSeconds must be between 30 and 86400.");
        if (value.AuthorityRevisionRetentionDays is < 7 or > 90)
            failures.Add("Interaction:AuthorityRevisionRetentionDays must be between 7 and 90.");
        if (value.AuthorityRevisionPruneBatchSize is < 1 or > 5000)
            failures.Add("Interaction:AuthorityRevisionPruneBatchSize must be between 1 and 5000.");
        if (value.ProjectionGapDrainMaximum is < 1 or > 1000)
            failures.Add("Interaction:ProjectionGapDrainMaximum must be between 1 and 1000.");
        if (value.ErasureBatchSize is < 1 or > 500)
            failures.Add("Interaction:ErasureBatchSize must be between 1 and 500.");
        if (value.ErasurePollSeconds is < 1 or > 3600)
            failures.Add("Interaction:ErasurePollSeconds must be between 1 and 3600.");
        if (value.ErasureLeaseSeconds is < 30 or > 3600)
            failures.Add("Interaction:ErasureLeaseSeconds must be between 30 and 3600.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

public static class InteractionInputGuard
{
    public static void ValidateStatusBatchCount(int count, int maximum)
    {
        if (count is < 1 || count > maximum)
            throw new InteractionValidationException($"Status batch must contain between 1 and {maximum} resources.");
    }
}
