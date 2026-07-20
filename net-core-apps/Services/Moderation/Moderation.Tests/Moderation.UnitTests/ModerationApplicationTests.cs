using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Moderation.Application.Dtos;
using Moderation.Application.Options;
using Moderation.Application.Features.Cases.Queries;
using Moderation.Application.Features.Privacy.Commands;
using Moderation.Contracts;
using Moderation.Domain.AggregatesModel;
using MessageBrokerContracts.Privacy.Restricted;
using Microsoft.Extensions.Options;

namespace Moderation.UnitTests;

public sealed class ModerationApplicationTests
{
    [Fact]
    public async Task Successful_first_triage_records_latency_and_an_exact_four_hour_breach()
    {
        var durations = new ConcurrentQueue<double>();
        long breachCount = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == "Inspacy.Platform"
                && instrument.Name is "moderation.first_response.duration"
                    or "moderation.first_response.breaches")
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, _, _) =>
        {
            if (instrument.Name == "moderation.first_response.duration")
                durations.Enqueue(measurement);
        });
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            if (instrument.Name == "moderation.first_response.breaches")
                Interlocked.Add(ref breachCount, measurement);
        });
        listener.Start();

        var store = new FakeModerationRepository();
        var pair = ModerationTestFactory.CreateCase();
        store.Reports.Add(pair.Report);
        store.Cases.Add(pair.Case);
        var service = ModerationTestFactory.CreateCaseCommands(
            store,
            new FakeModerationEventPublisher(),
            new FixedTimeProvider(ModerationTestFactory.Now.AddHours(5)));

        var result = await service.TriageAsync(
            pair.Case.Id,
            "triage-1",
            new TriageCaseRequest(CasePriority.High, "policy-spam", pair.Case.Version),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains(durations, seconds => seconds >= TimeSpan.FromHours(5).TotalSeconds);
        Assert.True(Volatile.Read(ref breachCount) >= 1);
    }

    [Fact]
    public async Task Sla_backlog_is_aggregate_only_and_reports_oldest_overdue_age()
    {
        var store = new FakeModerationRepository();
        var active = ModerationTestFactory.CreateCase();
        var terminal = ModerationTestFactory.CreateCase(CaseStatus.Decided);
        store.Cases.Add(active.Case);
        store.Cases.Add(terminal.Case);
        var clock = new FixedTimeProvider(ModerationTestFactory.Now.AddHours(25));
        var query = new GetModerationSlaBacklogQuery(store, clock);

        var result = await query.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ActiveCount);
        Assert.Equal(1, result.OverdueCount);
        Assert.Equal(TimeSpan.FromHours(1).TotalSeconds, result.OldestOverdueAgeSeconds);
    }

    [Fact]
    public async Task Report_submission_is_idempotent_and_returns_the_stable_receipt()
    {
        var store = new FakeModerationRepository();
        var publisher = new FakeModerationEventPublisher();
        var service = ModerationTestFactory.CreateReportCommands(store, publisher);

        var first = await service.SubmitReportAsync(
            "reporter-1",
            "stable-key-0001",
            ModerationTestFactory.Request(),
            TestContext.Current.CancellationToken);
        var replay = await service.SubmitReportAsync(
            "reporter-1",
            "stable-key-0001",
            ModerationTestFactory.Request(),
            TestContext.Current.CancellationToken);

        Assert.True(first.IsSuccess);
        Assert.True(replay.IsSuccess);
        Assert.False(first.Value!.IsDuplicate);
        Assert.True(replay.Value!.IsDuplicate);
        Assert.Equal(first.Value.ReportId, replay.Value.ReportId);
        Assert.Single(store.Reports);
        Assert.Single(store.Cases);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task Idempotency_key_reuse_with_a_different_hash_is_a_conflict()
    {
        var store = new FakeModerationRepository();
        var service = ModerationTestFactory.CreateReportCommands(store, new FakeModerationEventPublisher());
        await service.SubmitReportAsync(
            "reporter-1",
            "stable-key-0001",
            ModerationTestFactory.Request(),
            TestContext.Current.CancellationToken);

        var conflict = await service.SubmitReportAsync(
            "reporter-1",
            "stable-key-0001",
            ModerationTestFactory.Request("post_999"),
            TestContext.Current.CancellationToken);

        Assert.False(conflict.IsSuccess);
        Assert.Equal(ModerationFailureKind.IdempotencyConflict, conflict.Failure!.Kind);
        Assert.Equal("idempotency-key-reused", conflict.Failure.Code);
    }

    [Fact]
    public async Task Reporter_status_never_discloses_case_internals_and_enforces_ownership()
    {
        var store = new FakeModerationRepository();
        var pair = ModerationTestFactory.CreateCase(CaseStatus.Investigating);
        store.Reports.Add(pair.Report);
        store.Cases.Add(pair.Case);
        var service = ModerationTestFactory.CreateReportQueries(store);

        var owner = await service.GetReporterStatusAsync(
            pair.Report.Id,
            "reporter-1",
            TestContext.Current.CancellationToken);
        var other = await service.GetReporterStatusAsync(
            pair.Report.Id,
            "reporter-2",
            TestContext.Current.CancellationToken);

        Assert.True(owner.IsSuccess);
        Assert.Equal("InReview", owner.Value!.Status);
        Assert.False(owner.Value.CanAppeal);
        Assert.False(other.IsSuccess);
        Assert.Equal(ModerationFailureKind.NotFound, other.Failure!.Kind);
    }

    [Fact]
    public async Task Decision_event_is_staged_before_save_and_contains_monotonic_version()
    {
        var store = new FakeModerationRepository();
        var pair = ModerationTestFactory.CreateCase(CaseStatus.DecisionPending);
        store.Reports.Add(pair.Report);
        store.Cases.Add(pair.Case);
        var publisher = new FakeModerationEventPublisher();
        store.BeforeSave = () =>
        {
            Assert.Single(publisher.Decisions);
            Assert.Single(publisher.Actions);
        };
        var service = ModerationTestFactory.CreateCaseCommands(store, publisher);

        var result = await service.DecideAsync(
            pair.Case.Id,
            "decision-1",
            new DecideCaseRequest(
                ModerationDecisionAction.RemoveContent,
                "policy-confirmed",
                pair.Case.Version),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(1, publisher.Decisions[0].DecisionVersion);
        Assert.Equal(pair.Case.Id, publisher.Decisions[0].CaseId);
        Assert.Equal(ModerationDecisionAction.RemoveContent, publisher.Actions[0].Action);
    }

    [Fact]
    public async Task Stale_application_command_returns_current_version_without_writing()
    {
        var store = new FakeModerationRepository();
        var pair = ModerationTestFactory.CreateCase();
        store.Reports.Add(pair.Report);
        store.Cases.Add(pair.Case);
        var service = ModerationTestFactory.CreateCaseCommands(store, new FakeModerationEventPublisher());

        var result = await service.TriageAsync(
            pair.Case.Id,
            "triage-1",
            new TriageCaseRequest(CasePriority.High, "policy-spam", 99),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModerationFailureKind.VersionConflict, result.Failure!.Kind);
        Assert.Equal(1, result.Failure.CurrentVersion);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task Workflow_rejections_and_boundary_cancellation_emit_bounded_reasons_only()
    {
        var measurements = new ConcurrentQueue<(string Instrument, string Value, string[] Keys)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == "Inspacy.Platform"
                && instrument.Name is "moderation.workflow.rejections"
                    or "moderation.operation.boundary_cancellations")
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            var snapshot = tags.ToArray();
            measurements.Enqueue((
                instrument.Name,
                snapshot.Single().Value?.ToString() ?? string.Empty,
                snapshot.Select(tag => tag.Key).Order(StringComparer.Ordinal).ToArray()));
        });
        listener.Start();

        var store = new FakeModerationRepository();
        var pair = ModerationTestFactory.CreateCase();
        store.Reports.Add(pair.Report);
        store.Cases.Add(pair.Case);
        var service = ModerationTestFactory.CreateCaseCommands(
            store,
            new FakeModerationEventPublisher());

        var stale = await service.TriageAsync(
            pair.Case.Id,
            "triage-1",
            new TriageCaseRequest(CasePriority.High, "policy-spam", 99),
            TestContext.Current.CancellationToken);
        var invalidTransition = await service.AssignAsync(
            pair.Case.Id,
            "triage-1",
            new AssignCaseRequest("investigator-1", "skill-match", pair.Case.Version),
            TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.TriageAsync(
            pair.Case.Id,
            "triage-1",
            new TriageCaseRequest(CasePriority.High, "policy-spam", pair.Case.Version),
            cancellation.Token));

        Assert.Equal(ModerationFailureKind.VersionConflict, stale.Failure!.Kind);
        Assert.Equal(ModerationFailureKind.Conflict, invalidTransition.Failure!.Kind);
        Assert.Contains(measurements, item => item is
            ("moderation.workflow.rejections", "version_conflict", _));
        Assert.Contains(measurements, item => item is
            ("moderation.workflow.rejections", "workflow_conflict", _));
        Assert.Contains(measurements, item => item is
            ("moderation.operation.boundary_cancellations", "application_mutation", _));
        Assert.All(
            measurements.Where(item => item.Instrument == "moderation.workflow.rejections"),
            item => Assert.Equal(["reason"], item.Keys));
        Assert.All(
            measurements.Where(item => item.Instrument == "moderation.operation.boundary_cancellations"),
            item => Assert.Equal(["boundary"], item.Keys));
    }

    [Fact]
    public async Task Cancellation_is_propagated_before_store_or_publish_work()
    {
        var store = new FakeModerationRepository();
        var publisher = new FakeModerationEventPublisher();
        var service = ModerationTestFactory.CreateReportCommands(store, publisher);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SubmitReportAsync(
                "reporter-1",
                "stable-key-0001",
                ModerationTestFactory.Request(),
                cancellation.Token));

        Assert.Empty(store.Reports);
        Assert.Empty(publisher.Decisions);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task Escalation_is_bounded_and_publishes_only_restricted_coordinates()
    {
        var store = new FakeModerationRepository();
        for (var index = 0; index < 5; index++)
        {
            var pair = ModerationTestFactory.CreateCase();
            store.Reports.Add(pair.Report);
            store.Cases.Add(pair.Case);
        }
        var publisher = new FakeModerationEventPublisher();
        var clock = new FixedTimeProvider(ModerationTestFactory.Now.AddDays(2));
        var service = ModerationTestFactory.CreateEscalationCommand(
            store,
            publisher,
            clock,
            escalation: new ModerationEscalationOptions { MaxBatchSize = 2, PollIntervalSeconds = 5 });

        var count = await service.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, count);
        Assert.Equal(2, publisher.Escalations.Count);
        Assert.Equal(1, store.SaveCount);
        Assert.All(publisher.Escalations, message => Assert.Equal("Critical", message.Priority));
    }

    [Fact]
    public async Task Cursor_pagination_is_bounded_and_rejects_tampering()
    {
        var store = new FakeModerationRepository();
        for (var index = 0; index < 120; index++)
        {
            var pair = ModerationTestFactory.CreateCase();
            store.Reports.Add(pair.Report);
            store.Cases.Add(pair.Case);
        }
        var service = ModerationTestFactory.CreateCaseQueries(store);

        var page = await service.ListInternalCasesAsync(
            null,
            1000,
            TestContext.Current.CancellationToken);
        var invalid = await service.ListInternalCasesAsync(
            "not-a-valid-cursor",
            50,
            TestContext.Current.CancellationToken);

        Assert.True(page.IsSuccess);
        Assert.Equal(100, page.Value!.Items.Count);
        Assert.NotNull(page.Value.NextCursor);
        Assert.False(invalid.IsSuccess);
        Assert.Equal(ModerationFailureKind.Validation, invalid.Failure!.Kind);
    }

    [Fact]
    public async Task Legal_hold_placement_and_release_are_audited_with_versions()
    {
        var store = new FakeModerationRepository();
        var pair = ModerationTestFactory.CreateCase();
        store.Reports.Add(pair.Report);
        store.Cases.Add(pair.Case);
        var service = ModerationTestFactory.CreateLegalHoldCommands(store, new FakeModerationEventPublisher());

        var placed = await service.PlaceLegalHoldAsync(
            pair.Case.Id,
            "admin-1",
            new LegalHoldRequest("legal-request", 1),
            TestContext.Current.CancellationToken);
        var released = await service.ReleaseLegalHoldAsync(
            pair.Case.Id,
            "admin-1",
            new LegalHoldRequest("legal-release", 2),
            TestContext.Current.CancellationToken);

        Assert.True(placed.IsSuccess);
        Assert.True(released.IsSuccess);
        Assert.False(released.Value!.IsActive);
        Assert.Equal(3, pair.Case.Version);
        Assert.Contains(pair.Case.AuditEntries, entry => entry.Action == "legal-hold.placed");
        Assert.Contains(pair.Case.AuditEntries, entry => entry.Action == "legal-hold.released");
    }

    [Fact]
    public async Task Evidence_inputs_are_references_only_and_are_strictly_validated()
    {
        var service = ModerationTestFactory.CreateReportCommands(
            new FakeModerationRepository(),
            new FakeModerationEventPublisher());
        var malformed = new SubmitModerationReportRequestDto(
            new ModeratedResourceReferenceDto(ModeratedResourceKindDto.Post, "post_1"),
            ReportReasonCodeDto.Spam,
            [new EvidenceReferenceDto(EvidenceReferenceKindDto.ContentSnapshot, "snapshot", "not-a-hash")]);

        var result = await service.SubmitReportAsync(
            "reporter-1",
            "stable-key-0001",
            malformed,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModerationFailureKind.Validation, result.Failure!.Kind);
    }

    [Fact]
    public async Task User_erasure_blocks_new_reports_deletes_unheld_data_and_acknowledges_once()
    {
        var store = new FakeModerationRepository();
        var publisher = new FakeModerationEventPublisher();
        var clock = new FixedTimeProvider(ModerationTestFactory.Now);
        var reports = ModerationTestFactory.CreateReportCommands(
            store,
            publisher,
            clock);
        var firstReport = await reports.SubmitReportAsync(
            "42",
            "stable-key-0042",
            ModerationTestFactory.Request(),
            TestContext.Current.CancellationToken);
        Assert.True(firstReport.IsSuccess);

        var request = new UserErasureRequested
        {
            EventId = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            UserId = 42,
            ErasureVersion = 1,
            RequestedAtUtc = clock.GetUtcNow(),
        };
        var schedule = new ScheduleModerationUserErasureCommand(
            store,
            publisher,
            clock);
        Assert.True(await schedule.Handle(
            request,
            TestContext.Current.CancellationToken));
        Assert.False(await schedule.Handle(
            request,
            TestContext.Current.CancellationToken));

        var blocked = await reports.SubmitReportAsync(
            "00042",
            "stable-key-0043",
            ModerationTestFactory.Request(),
            TestContext.Current.CancellationToken);
        Assert.False(blocked.IsSuccess);
        Assert.Equal(ModerationFailureKind.Forbidden, blocked.Failure!.Kind);

        var process = new ProcessModerationUserErasureCommand(
            store,
            publisher,
            clock,
            Options.Create(new ModerationErasureOptions
            {
                Enabled = true,
                ApprovalReference = "PRIV-42",
                MaxBatchSize = 1,
                LeaseSeconds = 30,
                LegalHoldReviewSeconds = 60,
            }));
        Assert.True(await process.Handle(
            "moderation-erasure-worker",
            TestContext.Current.CancellationToken) > 0);
        Assert.Empty(store.Reports);
        Assert.Empty(store.Cases);
        var tombstone = Assert.Single(store.UserErasures);
        Assert.Equal(ModerationUserErasureStage.Completed, tombstone.Stage);
        Assert.Equal(0, tombstone.HeldCaseCount);
        var completion = Assert.Single(publisher.Erasures);
        Assert.Equal(request.RequestId, completion.RequestId);
        Assert.Equal(UserErasureOutcomes.Completed, completion.Outcome);
    }

    [Fact]
    public async Task Active_legal_hold_is_retained_and_revisited_after_release_without_duplicate_ack()
    {
        var store = new FakeModerationRepository();
        var publisher = new FakeModerationEventPublisher();
        var clock = new FixedTimeProvider(ModerationTestFactory.Now);
        var reports = ModerationTestFactory.CreateReportCommands(
            store,
            publisher,
            clock);
        Assert.True((await reports.SubmitReportAsync(
            "43",
            "stable-key-0043",
            ModerationTestFactory.Request(),
            TestContext.Current.CancellationToken)).IsSuccess);
        var moderationCase = Assert.Single(store.Cases);
        var hold = LegalHold.Place(
            moderationCase.Id,
            new ResourceReference(
                moderationCase.ResourceKind,
                moderationCase.ResourceId),
            "legal-admin",
            "litigation-hold",
            clock.GetUtcNow());
        store.AddLegalHold(hold);

        var schedule = new ScheduleModerationUserErasureCommand(
            store,
            publisher,
            clock);
        await schedule.Handle(
            new UserErasureRequested
            {
                EventId = Guid.NewGuid(),
                RequestId = Guid.NewGuid(),
                UserId = 43,
                ErasureVersion = 1,
                RequestedAtUtc = clock.GetUtcNow(),
            },
            TestContext.Current.CancellationToken);
        var process = new ProcessModerationUserErasureCommand(
            store,
            publisher,
            clock,
            Options.Create(new ModerationErasureOptions
            {
                Enabled = true,
                ApprovalReference = "PRIV-43",
                MaxBatchSize = 1,
                LeaseSeconds = 30,
                LegalHoldReviewSeconds = 60,
            }));

        Assert.True(await process.Handle(
            "moderation-erasure-worker",
            TestContext.Current.CancellationToken) > 0);
        Assert.Single(store.Cases);
        Assert.Equal(1, Assert.Single(store.UserErasures).HeldCaseCount);
        Assert.Single(publisher.Erasures);

        clock.UtcNow = clock.UtcNow.AddMinutes(2);
        Assert.Equal(0, await process.Handle(
            "moderation-erasure-worker",
            TestContext.Current.CancellationToken));
        Assert.Single(publisher.Erasures);

        hold.Release("legal-admin", "hold-released", clock.GetUtcNow());
        clock.UtcNow = clock.UtcNow.AddMinutes(2);
        Assert.True(await process.Handle(
            "moderation-erasure-worker",
            TestContext.Current.CancellationToken) > 0);
        Assert.Empty(store.Cases);
        Assert.Empty(store.Reports);
        Assert.Equal(0, Assert.Single(store.UserErasures).HeldCaseCount);
        Assert.Single(publisher.Erasures);
    }
}
