using BuildingBlocks.Security;

namespace Moderation.Application.Options;

public sealed class ModerationPrivacyOptions
{
    public const string SectionName = "Moderation:Privacy";
    public string SubjectLookupKey { get; set; } = string.Empty;

    public static bool Validate(ModerationPrivacyOptions options) =>
        Base64KeyMaterial.TryDecode(
            options.SubjectLookupKey,
            out _,
            rejectSurroundingWhitespace: false);
}

public sealed class ModerationErasureOptions
{
    public const string SectionName = "Moderation:Erasure";
    public bool Enabled { get; set; }
    public string ApprovalReference { get; set; } = string.Empty;
    public int PollIntervalSeconds { get; set; } = 30;
    public int MaxBatchSize { get; set; } = 100;
    public int LeaseSeconds { get; set; } = 120;
    public int LegalHoldReviewSeconds { get; set; } = 86_400;

    public static bool Validate(ModerationErasureOptions options) =>
        options.PollIntervalSeconds is >= 5 and <= 86_400
        && options.MaxBatchSize is >= 1 and <= 500
        && options.LeaseSeconds is >= 30 and <= 3_600
        && options.LegalHoldReviewSeconds is >= 60 and <= 604_800
        && (!options.Enabled
            || ModerationRetentionOptions.IsApprovalReferenceValid(
                options.ApprovalReference));
}

public sealed class ModerationRetentionOptions
{
    public const string SectionName = "Moderation:Retention";
    public bool Enabled { get; set; }
    public string ApprovalReference { get; set; } = string.Empty;
    public int ReportsAndCasesDays { get; set; } = 730;
    public int AuditDays { get; set; } = 2555;
    public int PollIntervalSeconds { get; set; } = 300;
    public int MaxBatchSize { get; set; } = 100;
    public int LeaseSeconds { get; set; } = 120;

    public static bool Validate(ModerationRetentionOptions options) =>
        options.ReportsAndCasesDays is >= 730 and <= 3_650
        && options.AuditDays is >= 2_555 and <= 7_300
        && options.AuditDays >= options.ReportsAndCasesDays
        && options.PollIntervalSeconds is >= 30 and <= 86_400
        && options.MaxBatchSize is >= 1 and <= 500
        && options.LeaseSeconds is >= 60 and <= 3_600
        && (!options.Enabled || IsApprovalReferenceValid(options.ApprovalReference));

    internal static bool IsApprovalReferenceValid(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.' or ':' or '/');
}

public sealed class ModerationDecisionRevisionRetentionOptions
{
    public const string SectionName = "DecisionExport:RevisionRetention";
    public int RetentionDays { get; set; } = 7;
    public int MaxBatchSize { get; set; } = 500;

    public static bool Validate(ModerationDecisionRevisionRetentionOptions options) =>
        options.RetentionDays is >= 7 and <= 90
        && options.MaxBatchSize is >= 1 and <= 5000;
}

public sealed class ModerationSlaOptions
{
    public const string SectionName = "Moderation:Sla";
    public const int FirstResponseMaximumMinutes = 240;
    public int ReceivedMinutes { get; set; } = FirstResponseMaximumMinutes;
    public int LowPriorityMinutes { get; set; } = 2880;
    public int NormalPriorityMinutes { get; set; } = 1440;
    public int HighPriorityMinutes { get; set; } = 240;
    public int CriticalPriorityMinutes { get; set; } = 60;
    public int InvestigationMinutes { get; set; } = 1440;
    public int DecisionMinutes { get; set; } = 480;
    public int AppealMinutes { get; set; } = 1440;

    public static bool Validate(ModerationSlaOptions options) =>
        options.ReceivedMinutes is > 0 and <= FirstResponseMaximumMinutes
        && options.LowPriorityMinutes is > 0 and <= 10080
        && options.NormalPriorityMinutes is > 0 and <= 10080
        && options.HighPriorityMinutes is > 0 and <= 10080
        && options.CriticalPriorityMinutes is > 0 and <= 10080
        && options.InvestigationMinutes is > 0 and <= 10080
        && options.DecisionMinutes is > 0 and <= 10080
        && options.AppealMinutes is > 0 and <= 10080;
}

public sealed class ModerationEscalationOptions
{
    public const string SectionName = "Moderation:Escalation";
    public int PollIntervalSeconds { get; set; } = 30;
    public int MaxBatchSize { get; set; } = 100;

    public static bool Validate(ModerationEscalationOptions options) =>
        options.PollIntervalSeconds is >= 5 and <= 3600
        && options.MaxBatchSize is >= 1 and <= 500;
}
