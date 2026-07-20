using Moderation.Domain.AggregatesModel;
using Moderation.Domain.Exceptions;

namespace Moderation.UnitTests;

public sealed class ModerationDomainTests
{
    private static readonly HashSet<(CaseStatus From, CaseStatus To)> AllowedTransitions =
    [
        (CaseStatus.Received, CaseStatus.Triaged),
        (CaseStatus.Triaged, CaseStatus.Investigating),
        (CaseStatus.Investigating, CaseStatus.DecisionPending),
        (CaseStatus.DecisionPending, CaseStatus.Decided),
        (CaseStatus.Decided, CaseStatus.Appealed),
        (CaseStatus.Decided, CaseStatus.Closed),
        (CaseStatus.Appealed, CaseStatus.Investigating),
    ];

    public static TheoryData<CaseStatus, CaseStatus, bool> TransitionMatrix()
    {
        var data = new TheoryData<CaseStatus, CaseStatus, bool>();
        foreach (var from in Enum.GetValues<CaseStatus>())
            foreach (var to in Enum.GetValues<CaseStatus>())
                data.Add(from, to, AllowedTransitions.Contains((from, to)));
        return data;
    }

    [Theory]
    [MemberData(nameof(TransitionMatrix))]
    public void Transition_matrix_is_explicit_and_fail_closed(
        CaseStatus from,
        CaseStatus to,
        bool expected) =>
        Assert.Equal(expected, ModerationCase.CanTransition(from, to));

    [Fact]
    public void Complete_workflow_increments_versions_and_builds_valid_audit_chain()
    {
        var (_, moderationCase) = ModerationTestFactory.CreateCase(CaseStatus.Decided);

        Assert.Equal(CaseStatus.Decided, moderationCase.Status);
        Assert.Equal(6, moderationCase.Version);
        Assert.Equal(1, moderationCase.DecisionVersion);
        Assert.Equal(DecisionAction.RemoveContent, moderationCase.DecisionAction);
        Assert.Equal(6, moderationCase.AuditEntries.Count);

        var previous = string.Empty;
        foreach (var entry in moderationCase.AuditEntries)
        {
            Assert.Equal(previous, entry.PreviousHash);
            Assert.True(entry.HasValidHash());
            previous = entry.Hash;
        }
        Assert.Equal(previous, moderationCase.LastAuditHash);
    }

    [Fact]
    public void Stale_expected_version_is_rejected_without_mutation()
    {
        var (_, moderationCase) = ModerationTestFactory.CreateCase();

        var exception = Assert.Throws<ModerationConcurrencyException>(() =>
            moderationCase.Triage(
                "triage-1",
                "policy-spam",
                CasePriority.Normal,
                42,
                ModerationTestFactory.Now,
                ModerationTestFactory.Now.AddHours(1)));

        Assert.Equal(42, exception.ExpectedVersion);
        Assert.Equal(1, exception.ActualVersion);
        Assert.Equal(CaseStatus.Received, moderationCase.Status);
        Assert.Single(moderationCase.AuditEntries);
    }

    [Fact]
    public void Only_assigned_investigator_can_advance_investigation()
    {
        var (_, moderationCase) = ModerationTestFactory.CreateCase(CaseStatus.Triaged);
        moderationCase.Assign(
            "triage-1",
            "investigator-1",
            "skill-match",
            2,
            ModerationTestFactory.Now.AddMinutes(2));

        Assert.Throws<ModerationConflictException>(() =>
            moderationCase.StartInvestigation(
                "investigator-2",
                "review-started",
                3,
                ModerationTestFactory.Now.AddMinutes(3),
                ModerationTestFactory.Now.AddHours(3)));
        Assert.Equal(CaseStatus.Triaged, moderationCase.Status);
    }

    [Fact]
    public void Resource_reference_rejects_urls_and_unknown_kinds()
    {
        Assert.Throws<ModerationValidationException>(() =>
            new ResourceReference(ResourceKind.Post, "https://storage/evidence"));
        Assert.Throws<ModerationValidationException>(() =>
            new ResourceReference((ResourceKind)999, "post_123"));
    }

    [Fact]
    public void Legal_hold_is_record_scoped_and_release_is_one_way()
    {
        var (_, moderationCase) = ModerationTestFactory.CreateCase();
        var hold = LegalHold.Place(
            moderationCase.Id,
            new ResourceReference(moderationCase.ResourceKind, moderationCase.ResourceId),
            "admin-1",
            "litigation-request",
            ModerationTestFactory.Now);

        Assert.True(hold.IsActive);
        Assert.Equal(moderationCase.Id.ToString("D"), hold.ActiveCaseKey);
        hold.Release("admin-1", "request-withdrawn", ModerationTestFactory.Now.AddDays(1));
        Assert.False(hold.IsActive);
        Assert.Null(hold.ActiveCaseKey);
        Assert.Throws<ModerationConflictException>(() =>
            hold.Release("admin-1", "duplicate-release", ModerationTestFactory.Now.AddDays(2)));
    }

    [Fact]
    public void Decided_cases_are_not_escalated_by_the_sla_worker()
    {
        var (_, moderationCase) = ModerationTestFactory.CreateCase(CaseStatus.Decided);

        Assert.False(moderationCase.EscalateIfOverdue("moderation-sla-worker", ModerationTestFactory.Now.AddDays(30)));
        Assert.Null(moderationCase.EscalatedAtUtc);
    }

    [Fact]
    public void User_erasure_tombstone_is_permanent_ordered_and_hold_aware()
    {
        var now = ModerationTestFactory.Now;
        var requestId = Guid.NewGuid();
        var erasure = ModerationUserErasure.Schedule(
            42,
            requestId,
            1,
            now,
            now);

        Assert.Equal("42", erasure.ReporterId);
        Assert.Equal(ModerationUserErasureStage.Active, erasure.Stage);
        Assert.Equal(1, erasure.Acquire(
            "moderation-erasure-worker",
            now,
            TimeSpan.FromMinutes(2)));
        Assert.True(erasure.Complete(
            now,
            heldCaseCount: 1,
            TimeSpan.FromHours(1)));
        Assert.Equal(ModerationUserErasureStage.Completed, erasure.Stage);
        Assert.Equal(1, erasure.HeldCaseCount);
        Assert.Equal(now.AddHours(1), erasure.NextAttemptAtUtc);

        erasure.Supersede(
            Guid.NewGuid(),
            2,
            now.AddMinutes(1),
            now.AddMinutes(2));
        Assert.Equal(ModerationUserErasureStage.Active, erasure.Stage);
        Assert.Null(erasure.CompletedAtUtc);
        Assert.Equal(2, erasure.ErasureVersion);
        Assert.Throws<ModerationConflictException>(() => erasure.Supersede(
            requestId,
            2,
            now,
            now));
    }
}
