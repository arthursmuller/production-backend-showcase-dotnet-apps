namespace Moderation.Domain.AggregatesModel;

public enum ResourceKind
{
    Post = 1,
    Comment = 2,
    Profile = 3,
    Message = 4,
    Media = 5,
}

public enum EvidenceKind
{
    ObjectStorage = 1,
    ContentSnapshot = 2,
    ExternalCase = 3,
}

public enum ReportReason
{
    Spam = 1,
    Harassment = 2,
    Hate = 3,
    Violence = 4,
    SexualContent = 5,
    SelfHarm = 6,
    Fraud = 7,
    Impersonation = 8,
    Privacy = 9,
    OtherPolicyViolation = 10,
}

public enum CaseStatus
{
    Received = 1,
    Triaged = 2,
    Investigating = 3,
    DecisionPending = 4,
    Decided = 5,
    Appealed = 6,
    Closed = 7,
}

public enum CasePriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Critical = 4,
}

public enum DecisionAction
{
    NoAction = 1,
    LimitDistribution = 2,
    RemoveContent = 3,
    SuspendAccount = 4,
    PreserveForReview = 5,
}
