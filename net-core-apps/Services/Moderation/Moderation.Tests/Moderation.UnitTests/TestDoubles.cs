using Microsoft.Extensions.Options;
using Moderation.Application.Dtos;
using Moderation.Application.Features.Cases.Commands;
using Moderation.Application.Features.Cases.Queries;
using Moderation.Application.Features.Reports.Commands;
using Moderation.Application.Features.Reports.Queries;
using Moderation.Application.Features.Privacy;
using Moderation.Application.Options;
using Moderation.Application.Providers;
using Moderation.Contracts;
using Moderation.Contracts.Restricted;
using MessageBrokerContracts.Privacy.Restricted;
using Moderation.Domain.AggregatesModel;
using Moderation.Domain.Repositories;

namespace Moderation.UnitTests;

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
    public override DateTimeOffset GetUtcNow() => UtcNow;
}

internal sealed class FakeModerationRepository
    : IModerationRepository,
      IModerationUserErasureStore
{
    public List<ModerationReport> Reports { get; } = [];
    public List<ModerationCase> Cases { get; } = [];
    public List<LegalHold> LegalHolds { get; } = [];
    public List<ModerationCaseMessage> Messages { get; } = [];
    public List<ModerationUserErasure> UserErasures { get; } = [];
    public int SaveCount { get; private set; }
    public CancellationToken LastToken { get; private set; }
    public Action? BeforeSave { get; set; }
    public bool ThrowDuplicateOnNextSave { get; set; }

    public Task<ModerationReport?> FindReportByIdempotencyAsync(
        string reporterId,
        string idempotencyKeyHash,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        return Task.FromResult(Reports.SingleOrDefault(report =>
            report.ReporterId == reporterId && report.IdempotencyKeyHash == idempotencyKeyHash));
    }

    public Task<ModerationReport?> GetReportAsync(Guid reportId, CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        return Task.FromResult(Reports.SingleOrDefault(report => report.Id == reportId));
    }

    public Task<ModerationCase?> GetCaseAsync(Guid caseId, CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        return Task.FromResult(Cases.SingleOrDefault(item => item.Id == caseId));
    }

    public Task<ModerationCase?> GetCaseByReportAsync(Guid reportId, CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        return Task.FromResult(Cases.SingleOrDefault(item => item.ReportId == reportId));
    }

    public Task<LegalHold?> GetActiveLegalHoldAsync(Guid caseId, CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        return Task.FromResult(LegalHolds.SingleOrDefault(item => item.CaseId == caseId && item.IsActive));
    }

    public Task<IReadOnlyList<ModerationCase>> GetCasesPageAsync(
        DateTimeOffset? afterReceivedAtUtc,
        Guid? afterId,
        int take,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        var query = Cases.AsEnumerable();
        if (afterReceivedAtUtc.HasValue && afterId.HasValue)
        {
            query = query.Where(item => item.ReceivedAtUtc < afterReceivedAtUtc.Value
                || (item.ReceivedAtUtc == afterReceivedAtUtc.Value && item.Id.CompareTo(afterId.Value) < 0));
        }
        return Task.FromResult<IReadOnlyList<ModerationCase>>(query
            .OrderByDescending(item => item.ReceivedAtUtc)
            .ThenByDescending(item => item.Id)
            .Take(take)
            .ToArray());
    }

    public Task<IReadOnlyList<ModerationCase>> GetOverdueCasesAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        return Task.FromResult<IReadOnlyList<ModerationCase>>(Cases
            .Where(item => item.HasActiveSla && item.EscalatedAtUtc is null && item.SlaDueAtUtc <= now)
            .OrderBy(item => item.SlaDueAtUtc)
            .Take(take)
            .ToArray());
    }

    public Task<ModerationSlaBacklogState> GetSlaBacklogAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        var active = Cases.Where(item => item.HasActiveSla).ToArray();
        var overdue = active.Where(item => item.SlaDueAtUtc <= now).ToArray();
        return Task.FromResult(new ModerationSlaBacklogState(
            active.LongLength,
            overdue.LongLength,
            overdue.Length == 0 ? null : overdue.Min(item => item.SlaDueAtUtc)));
    }

    public Task<IReadOnlyList<ModerationAuditEntry>> GetAuditAsync(
        Guid caseId,
        int take,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        return Task.FromResult<IReadOnlyList<ModerationAuditEntry>>(Cases
            .Single(item => item.Id == caseId)
            .AuditEntries
            .OrderByDescending(item => item.CaseVersion)
            .Take(take)
            .ToArray());
    }

    public Task<IReadOnlyList<ModerationCaseMessage>> GetMessagesAsync(
        Guid caseId,
        int take,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        return Task.FromResult<IReadOnlyList<ModerationCaseMessage>>(Messages
            .Where(message => message.CaseId == caseId)
            .OrderByDescending(message => message.CreatedAtUtc)
            .Take(take)
            .ToArray());
    }

    public void AddReport(ModerationReport report) => Reports.Add(report);
    public void AddCase(ModerationCase moderationCase) => Cases.Add(moderationCase);
    public void AddLegalHold(LegalHold legalHold) => LegalHolds.Add(legalHold);
    public void AddMessage(ModerationCaseMessage message) => Messages.Add(message);

    public void ClearTracking()
    {
        // In-memory objects have no tracking state.
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        BeforeSave?.Invoke();
        if (ThrowDuplicateOnNextSave)
        {
            ThrowDuplicateOnNextSave = false;
            throw new DuplicateModerationReportException();
        }
        SaveCount++;
        return Task.CompletedTask;
    }

    public ValueTask<IModerationUserErasureTransaction> BeginSerializableAsync(
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        return ValueTask.FromResult<IModerationUserErasureTransaction>(
            FakeErasureTransaction.Instance);
    }

    public Task<ModerationUserErasure?> GetForUpdateAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        return Task.FromResult(UserErasures.SingleOrDefault(
            erasure => erasure.UserId == userId));
    }

    public Task<bool> IsUserErasedAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        return Task.FromResult(UserErasures.Any(
            erasure => erasure.UserId == userId));
    }

    public void Add(ModerationUserErasure erasure) => UserErasures.Add(erasure);

    public Task<ModerationUserErasure?> LockNextDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        return Task.FromResult(UserErasures
            .Where(erasure => erasure.NextAttemptAtUtc <= now
                && (erasure.Stage == ModerationUserErasureStage.Active
                    || (erasure.Stage == ModerationUserErasureStage.Completed
                        && erasure.HeldCaseCount > 0))
                && (erasure.LeaseUntilUtc is null
                    || erasure.LeaseUntilUtc <= now))
            .OrderBy(erasure => erasure.NextAttemptAtUtc)
            .ThenBy(erasure => erasure.LastProcessedAtUtc ?? erasure.RequestedAtUtc)
            .ThenBy(erasure => erasure.UserId)
            .FirstOrDefault());
    }

    public Task<ModerationErasureDataBatch> ExecuteDataBatchAsync(
        long userId,
        long fencingToken,
        int maximum,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        var reporterId = userId.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        var candidates = Cases
            .Where(moderationCase => moderationCase.ReporterId == reporterId
                && !LegalHolds.Any(hold =>
                    hold.CaseId == moderationCase.Id && hold.IsActive))
            .OrderBy(moderationCase => moderationCase.ReceivedAtUtc)
            .Take(maximum)
            .ToArray();
        var caseIds = candidates.Select(item => item.Id).ToHashSet();
        var reportIds = candidates.Select(item => item.ReportId).ToHashSet();
        var messages = Messages.RemoveAll(message => caseIds.Contains(message.CaseId));
        var releasedHolds = LegalHolds.RemoveAll(hold =>
            caseIds.Contains(hold.CaseId) && !hold.IsActive);
        Cases.RemoveAll(moderationCase => caseIds.Contains(moderationCase.Id));
        Reports.RemoveAll(report => reportIds.Contains(report.Id));
        return Task.FromResult(new ModerationErasureDataBatch(
            candidates.Length,
            messages,
            candidates.Sum(item => item.AuditEntries.Count),
            0,
            releasedHolds,
            candidates.Length));
    }

    public Task<bool> HasDeletableDataAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        var reporterId = userId.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        return Task.FromResult(Cases.Any(moderationCase =>
            moderationCase.ReporterId == reporterId
            && !LegalHolds.Any(hold =>
                hold.CaseId == moderationCase.Id && hold.IsActive)));
    }

    public Task<int> CountActiveHeldCasesAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        var reporterId = userId.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        return Task.FromResult(Cases.Count(moderationCase =>
            moderationCase.ReporterId == reporterId
            && LegalHolds.Any(hold =>
                hold.CaseId == moderationCase.Id && hold.IsActive)));
    }

    private void Observe(CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
    }

    private sealed class FakeErasureTransaction
        : IModerationUserErasureTransaction
    {
        internal static FakeErasureTransaction Instance { get; } = new();
        public Task CommitAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class FakeModerationEventPublisher : IModerationEventPublisher
{
    public List<ModerationDecisionMade> Decisions { get; } = [];
    public List<ModerationActionApplied> Actions { get; } = [];
    public List<ModerationCaseEscalatedRestricted> Escalations { get; } = [];
    public List<ModerationCaseChangedRestricted> CaseChanges { get; } = [];
    public List<UserErasureCompleted> Erasures { get; } = [];

    public Task PublishDecisionAsync(ModerationDecisionMade message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Decisions.Add(message);
        return Task.CompletedTask;
    }

    public Task PublishActionAsync(ModerationActionApplied message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Actions.Add(message);
        return Task.CompletedTask;
    }

    public Task PublishEscalationAsync(
        ModerationCaseEscalatedRestricted message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Escalations.Add(message);
        return Task.CompletedTask;
    }

    public Task PublishCaseChangedAsync(
        ModerationCaseChangedRestricted message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CaseChanges.Add(message);
        return Task.CompletedTask;
    }

    public Task PublishErasureCompletedAsync(
        UserErasureCompleted message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Erasures.Add(message);
        return Task.CompletedTask;
    }
}

internal static class ModerationTestFactory
{
    internal static readonly DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
    internal const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal static readonly string PrivacyKey = Convert.ToBase64String(
        Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());

    internal static ModerationSubjectPseudonymizer CreateSubjectPseudonymizer() =>
        new(Options.Create(new ModerationPrivacyOptions
        {
            SubjectLookupKey = PrivacyKey,
        }));

    internal static ReportCommands CreateReportCommands(
        FakeModerationRepository repository,
        FakeModerationEventPublisher publisher,
        FixedTimeProvider? clock = null,
        ModerationSlaOptions? sla = null) =>
        new(
            repository,
            repository,
            publisher,
            clock ?? new FixedTimeProvider(Now),
            Options.Create(sla ?? new ModerationSlaOptions()));

    internal static ReportQueries CreateReportQueries(FakeModerationRepository repository) =>
        new(repository, repository);

    internal static CaseCommands CreateCaseCommands(
        FakeModerationRepository repository,
        FakeModerationEventPublisher publisher,
        FixedTimeProvider? clock = null,
        ModerationSlaOptions? sla = null) =>
        new(
            repository,
            publisher,
            clock ?? new FixedTimeProvider(Now),
            Options.Create(sla ?? new ModerationSlaOptions()));

    internal static CaseQueries CreateCaseQueries(FakeModerationRepository repository) => new(repository);

    internal static LegalHoldCommands CreateLegalHoldCommands(
        FakeModerationRepository repository,
        FakeModerationEventPublisher publisher,
        FixedTimeProvider? clock = null) =>
        new(repository, publisher, clock ?? new FixedTimeProvider(Now));

    internal static CaseMessageCommands CreateCaseMessageCommands(
        FakeModerationRepository repository,
        FakeModerationEventPublisher publisher,
        FixedTimeProvider? clock = null) =>
        new(repository, publisher, clock ?? new FixedTimeProvider(Now));

    internal static EscalateOverdueCasesCommand CreateEscalationCommand(
        FakeModerationRepository repository,
        FakeModerationEventPublisher publisher,
        FixedTimeProvider? clock = null,
        ModerationEscalationOptions? escalation = null) =>
        new(
            repository,
            publisher,
            clock ?? new FixedTimeProvider(Now),
            Options.Create(escalation ?? new ModerationEscalationOptions()));

    internal static (ModerationReport Report, ModerationCase Case) CreateCase(
        CaseStatus target = CaseStatus.Received)
    {
        var reportId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var resource = new ResourceReference(ResourceKind.Post, "post_123");
        var report = ModerationReport.Create(
            reportId,
            caseId,
            "reporter-1",
            resource,
            ReportReason.Spam,
            Hash,
            Hash,
            Now,
            []);
        var moderationCase = ModerationCase.Create(
            caseId,
            reportId,
            resource,
            "reporter-1",
            Now,
            Now.AddHours(24));

        if ((int)target >= (int)CaseStatus.Triaged)
            moderationCase.Triage("triage-1", "policy-spam", CasePriority.Normal, 1, Now.AddMinutes(1), Now.AddHours(12));
        if ((int)target >= (int)CaseStatus.Investigating)
        {
            moderationCase.Assign("triage-1", "investigator-1", "skill-match", 2, Now.AddMinutes(2));
            moderationCase.StartInvestigation("investigator-1", "review-started", 3, Now.AddMinutes(3), Now.AddHours(10));
        }
        if ((int)target >= (int)CaseStatus.DecisionPending)
            moderationCase.CompleteInvestigation("investigator-1", "evidence-reviewed", 4, Now.AddMinutes(4), Now.AddHours(8));
        if ((int)target >= (int)CaseStatus.Decided)
            moderationCase.Decide("decision-1", "policy-confirmed", DecisionAction.RemoveContent, 5, Now.AddMinutes(5));
        if (target == CaseStatus.Appealed)
            moderationCase.Appeal("reporter-1", "appeal-requested", 6, Now.AddMinutes(6), Now.AddHours(24));
        if (target == CaseStatus.Closed)
            moderationCase.Close("admin-1", "case-complete", 6, Now.AddMinutes(6));
        return (report, moderationCase);
    }

    internal static SubmitModerationReportRequestDto Request(string resourceId = "post_123") =>
        new(
            new ModeratedResourceReferenceDto(ModeratedResourceKindDto.Post, resourceId),
            ReportReasonCodeDto.Spam,
            [new EvidenceReferenceDto(EvidenceReferenceKindDto.ContentSnapshot, "snapshot_123", Hash)]);
}
