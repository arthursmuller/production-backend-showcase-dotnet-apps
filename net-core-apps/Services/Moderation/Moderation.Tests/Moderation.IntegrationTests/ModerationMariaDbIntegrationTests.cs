using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moderation.Application.Dtos;
using Moderation.Application.Features.Reports.Commands;
using Moderation.Application.Features.Privacy;
using Moderation.Application.Features.Privacy.Commands;
using Moderation.Application.Options;
using Moderation.Application.Providers;
using Moderation.Contracts;
using Moderation.Contracts.Restricted;
using Moderation.Domain.AggregatesModel;
using Moderation.Domain.Repositories;
using Moderation.Infra.Database.Ef;
using Moderation.Infra.Database.Ef.Repositories;
using Moderation.Infra.Providers;
using MySqlConnector;
using MessageBrokerContracts.Privacy.Restricted;

namespace Moderation.UnitTests;

[Collection(ModerationMariaDbCollection.Name)]
public sealed class ModerationMariaDbIntegrationTests
{
    private const string ConnectionEnvironment = "MODERATION_MARIADB_TEST_CONNECTION";

    public static bool HasIsolatedMariaDb =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironment));

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    [Trait("Category", "Integration")]
    public async Task Domain_audit_and_bus_outbox_commit_atomically_and_append_only_triggers_hold()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironment)!;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new ModerationSlaOptions()));
        services.AddSingleton(Options.Create(new ModerationEscalationOptions()));
        services.AddSingleton(ModerationTestFactory.CreateSubjectPseudonymizer());
        services.AddDbContext<ModerationDbContext>(options => options.UseMySql(
            connectionString,
            new MariaDbServerVersion(new Version(10, 6, 27))));
        services.AddScoped<IModerationRepository, ModerationRepository>();
        services.AddScoped<IModerationUserErasureStore, ModerationUserErasureStore>();
        services.AddScoped<IModerationEventPublisher, MassTransitModerationEventPublisher>();
        services.AddScoped<ReportCommands>();
        services.AddMassTransit(registration =>
        {
            registration.AddEntityFrameworkOutbox<ModerationDbContext>(outbox =>
            {
                outbox.UseMySql();
                outbox.UseBusOutbox();
            });
            registration.UsingInMemory((_, _) => { });
        });

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ModerationDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var initialOutbox = await CountAsync(context, "OutboxMessage", TestContext.Current.CancellationToken);
        var commands = scope.ServiceProvider.GetRequiredService<ReportCommands>();
        var requestId = Guid.NewGuid();

        var result = await commands.SubmitReportAsync(
            "integration-reporter",
            $"integration:{requestId:D}",
            new SubmitModerationReportRequestDto(
                new ModeratedResourceReferenceDto(ModeratedResourceKindDto.Comment, $"comment_{Guid.NewGuid():N}"),
                ReportReasonCodeDto.Spam,
                []),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var reportId = result.Value!.ReportId;
        context.ChangeTracker.Clear();
        Assert.Equal(1, await context.Reports.CountAsync(
            report => report.Id == reportId,
            TestContext.Current.CancellationToken));
        var moderationCase = await context.Cases.SingleAsync(
            item => item.ReportId == reportId,
            TestContext.Current.CancellationToken);
        var slaBacklog = await scope.ServiceProvider
            .GetRequiredService<IModerationRepository>()
            .GetSlaBacklogAsync(
                DateTimeOffset.UtcNow.AddDays(30),
                TestContext.Current.CancellationToken);
        Assert.True(slaBacklog.ActiveCount >= 1);
        Assert.True(slaBacklog.OverdueCount >= 1);
        Assert.NotNull(slaBacklog.OldestOverdueDueAtUtc);
        Assert.Equal(1, await context.AuditEntries.CountAsync(
            audit => audit.CaseId == moderationCase.Id,
            TestContext.Current.CancellationToken));
        Assert.Equal(
            initialOutbox + 1,
            await CountAsync(context, "OutboxMessage", TestContext.Current.CancellationToken));

        var update = $"UPDATE `ModerationAuditEntries` SET `Action`='tampered' WHERE `CaseId`='{moderationCase.Id:D}'";
        var exception = await Assert.ThrowsAsync<MySqlException>(() =>
            context.Database.ExecuteSqlRawAsync(update, TestContext.Current.CancellationToken));
        Assert.Equal("45000", exception.SqlState);

        var beforeRollbackReports = await context.Reports.CountAsync(TestContext.Current.CancellationToken);
        var beforeRollbackOutbox = await CountAsync(context, "OutboxMessage", TestContext.Current.CancellationToken);
        await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IModerationEventPublisher>();
            await publisher.PublishCaseChangedAsync(
                new ModerationCaseChangedRestricted(
                    Guid.NewGuid(), "Received", "rollback.probe", 1, DateTimeOffset.UtcNow),
                TestContext.Current.CancellationToken);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }
        context.ChangeTracker.Clear();

        Assert.Equal(beforeRollbackReports, await context.Reports.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(beforeRollbackOutbox, await CountAsync(context, "OutboxMessage", TestContext.Current.CancellationToken));
    }

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    [Trait("Category", "Integration")]
    public async Task Scheduled_erasure_survives_retention_reporter_redaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironment)!;
        var dbOptions = new DbContextOptionsBuilder<ModerationDbContext>()
            .UseMySql(
                connectionString,
                new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options;
        await using var context = new ModerationDbContext(dbOptions);
        await context.Database.MigrateAsync(cancellationToken);

        var now = new DateTimeOffset(2032, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);
        var userId = Random.Shared.Next(100_000_000, int.MaxValue);
        var reporterId = userId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var fixture = CreateClosedCase(
            now.AddYears(-4),
            includeMessage: true,
            reporterId: reporterId);
        context.AddRange(fixture.Report, fixture.Case);
        if (fixture.Message is not null)
            context.Add(fixture.Message);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        var pseudonymizer = ModerationTestFactory.CreateSubjectPseudonymizer();
        var erasureStore = new ModerationUserErasureStore(context, pseudonymizer);
        var publisher = new ErasureCapturingPublisher();
        var request = new UserErasureRequested
        {
            EventId = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            UserId = userId,
            ErasureVersion = 1,
            RequestedAtUtc = now,
        };
        var schedule = new ScheduleModerationUserErasureCommand(
            erasureStore,
            publisher,
            clock);
        Assert.True(await schedule.Handle(request, cancellationToken));

        var retention = new ModerationRetentionStore(context, pseudonymizer);
        await retention.ExecuteBatchAsync(
            now.AddDays(-730),
            now.AddDays(-2555),
            now,
            $"erasure-retention-{Guid.NewGuid():N}",
            TimeSpan.FromMinutes(2),
            maxBatchSize: 500,
            cancellationToken);
        context.ChangeTracker.Clear();

        var retainedCase = await context.Cases.AsNoTracking().SingleAsync(
            item => item.Id == fixture.Case.Id,
            cancellationToken);
        Assert.StartsWith("retained:", retainedCase.ReporterId, StringComparison.Ordinal);
        Assert.Equal(pseudonymizer.Create(userId), retainedCase.ErasureSubjectHash);
        Assert.True(await context.AuditEntries.AsNoTracking().AnyAsync(
            item => item.CaseId == fixture.Case.Id && item.ActorId == reporterId,
            cancellationToken));
        Assert.True(await erasureStore.HasDeletableDataAsync(userId, cancellationToken));

        var process = new ProcessModerationUserErasureCommand(
            erasureStore,
            publisher,
            clock,
            Options.Create(new ModerationErasureOptions
            {
                Enabled = true,
                ApprovalReference = "PRIV-RETENTION-1",
                MaxBatchSize = 2,
                LeaseSeconds = 60,
                LegalHoldReviewSeconds = 60,
            }));
        await DrainErasureAsync(process, cancellationToken);
        context.ChangeTracker.Clear();

        Assert.False(await context.Cases.AsNoTracking().AnyAsync(
            item => item.Id == fixture.Case.Id,
            cancellationToken));
        Assert.False(await context.Reports.AsNoTracking().AnyAsync(
            item => item.Id == fixture.Report.Id,
            cancellationToken));
        Assert.False(await context.AuditEntries.AsNoTracking().AnyAsync(
            item => item.CaseId == fixture.Case.Id,
            cancellationToken));
        Assert.Equal(ModerationUserErasureStage.Completed,
            (await context.UserErasures.AsNoTracking().SingleAsync(
                item => item.UserId == userId,
                cancellationToken)).Stage);
        Assert.Single(publisher.Erasures);
    }

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    [Trait("Category", "Integration")]
    public async Task Retention_is_bounded_fenced_and_excludes_active_legal_holds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironment)!;
        var options = new DbContextOptionsBuilder<ModerationDbContext>()
            .UseMySql(connectionString, new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options;
        await using var context = new ModerationDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);

        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var redactPair = CreateClosedCase(now.AddYears(-4), includeMessage: true);
        var purgePair = CreateClosedCase(now.AddYears(-10), includeMessage: false);
        var heldPair = CreateClosedCase(now.AddYears(-10), includeMessage: false);
        var activeHold = LegalHold.Place(
            heldPair.Case.Id,
            new ResourceReference(heldPair.Case.ResourceKind, heldPair.Case.ResourceId),
            "legal-approver",
            "litigation-hold",
            now.AddYears(-9));
        heldPair.Case.RecordLegalHold(
            "legal-approver",
            "litigation-hold",
            placed: true,
            heldPair.Case.Version,
            now.AddYears(-9));

        context.AddRange(redactPair.Report, redactPair.Case, purgePair.Report, purgePair.Case,
            heldPair.Report, heldPair.Case, activeHold);
        if (redactPair.Message is not null)
            context.Add(redactPair.Message);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        var store = new ModerationRetentionStore(
            context,
            ModerationTestFactory.CreateSubjectPseudonymizer());
        var result = await store.ExecuteBatchAsync(
            now.AddDays(-730),
            now.AddDays(-2555),
            now,
            $"integration-{Guid.NewGuid():N}",
            TimeSpan.FromMinutes(2),
            maxBatchSize: 100,
            cancellationToken);

        Assert.True(result.LeaseAcquired);
        Assert.True(result.CasesRedacted >= 1);
        Assert.True(result.CasesPurged >= 1);
        Assert.False(await context.Cases.AnyAsync(item => item.Id == purgePair.Case.Id, cancellationToken));
        Assert.False(await context.Reports.AnyAsync(item => item.Id == purgePair.Report.Id, cancellationToken));
        Assert.False(await context.AuditEntries.AnyAsync(item => item.CaseId == purgePair.Case.Id, cancellationToken));

        var redactedCase = await context.Cases.AsNoTracking().SingleAsync(
            item => item.Id == redactPair.Case.Id,
            cancellationToken);
        var redactedReport = await context.Reports.AsNoTracking().SingleAsync(
            item => item.Id == redactPair.Report.Id,
            cancellationToken);
        Assert.StartsWith("retained:", redactedCase.ReporterId, StringComparison.Ordinal);
        Assert.StartsWith("retained:", redactedReport.ReporterId, StringComparison.Ordinal);
        Assert.Equal(
            ModerationSubjectPseudonymizer.EncodedLength,
            redactedCase.ErasureSubjectHash?.Length);
        Assert.NotNull(redactedCase.RetentionRedactedAtUtc);
        Assert.False(await context.EvidenceReferences.AnyAsync(
            item => item.ReportId == redactPair.Report.Id,
            cancellationToken));
        Assert.False(await context.CaseMessages.AnyAsync(
            item => item.CaseId == redactPair.Case.Id,
            cancellationToken));
        Assert.True(await context.AuditEntries.AnyAsync(
            item => item.CaseId == redactPair.Case.Id,
            cancellationToken));

        Assert.True(await context.Cases.AnyAsync(item => item.Id == heldPair.Case.Id, cancellationToken));
        Assert.True(await context.EvidenceReferences.AnyAsync(
            item => item.ReportId == heldPair.Report.Id,
            cancellationToken));
        Assert.True(await context.LegalHolds.AnyAsync(
            item => item.CaseId == heldPair.Case.Id && item.IsActive,
            cancellationToken));

        var directDelete = await Assert.ThrowsAsync<MySqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM `ModerationAuditEntries` WHERE `CaseId` = {redactPair.Case.Id}",
                cancellationToken));
        Assert.Equal("45000", directDelete.SqlState);
    }

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    [Trait("Category", "Integration")]
    public async Task User_erasure_is_bounded_fenced_permanent_and_revisits_legal_holds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironment)!;
        var dbOptions = new DbContextOptionsBuilder<ModerationDbContext>()
            .UseMySql(
                connectionString,
                new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options;
        await using var context = new ModerationDbContext(dbOptions);
        await context.Database.MigrateAsync(cancellationToken);

        var now = new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);
        var userId = Random.Shared.Next(100_000_000, int.MaxValue);
        var reporterId = userId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var unheld = CreateErasureCase(reporterId, now, 'b', includeMessage: true);
        var held = CreateErasureCase(reporterId, now.AddSeconds(1), 'c', includeMessage: false);
        var legalHold = LegalHold.Place(
            held.Case.Id,
            new ResourceReference(held.Case.ResourceKind, held.Case.ResourceId),
            "legal-admin",
            "litigation-hold",
            now.AddMinutes(1));
        context.AddRange(
            unheld.Report,
            unheld.Case,
            held.Report,
            held.Case,
            legalHold);
        if (unheld.Message is not null)
            context.Add(unheld.Message);
        await context.SaveChangesAsync(cancellationToken);

        var store = new ModerationUserErasureStore(
            context,
            ModerationTestFactory.CreateSubjectPseudonymizer());
        var publisher = new ErasureCapturingPublisher();
        var request = new UserErasureRequested
        {
            EventId = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            UserId = userId,
            ErasureVersion = 1,
            RequestedAtUtc = now,
        };
        var schedule = new ScheduleModerationUserErasureCommand(
            store,
            publisher,
            clock);
        Assert.True(await schedule.Handle(request, cancellationToken));

        var blockedReports = new ReportCommands(
            new ModerationRepository(context),
            store,
            publisher,
            clock,
            Options.Create(new ModerationSlaOptions()));
        var blocked = await blockedReports.SubmitReportAsync(
            reporterId,
            $"blocked-{Guid.NewGuid():N}",
            ModerationTestFactory.Request(),
            cancellationToken);
        Assert.False(blocked.IsSuccess);
        Assert.Equal(ModerationFailureKind.Forbidden, blocked.Failure!.Kind);

        var process = new ProcessModerationUserErasureCommand(
            store,
            publisher,
            clock,
            Options.Create(new ModerationErasureOptions
            {
                Enabled = true,
                ApprovalReference = "PRIV-MARIA-1",
                MaxBatchSize = 1,
                LeaseSeconds = 60,
                LegalHoldReviewSeconds = 60,
            }));
        await DrainErasureAsync(process, cancellationToken);
        context.ChangeTracker.Clear();

        Assert.False(await context.Cases.AsNoTracking().AnyAsync(
            item => item.Id == unheld.Case.Id,
            cancellationToken));
        Assert.False(await context.Reports.AsNoTracking().AnyAsync(
            item => item.Id == unheld.Report.Id,
            cancellationToken));
        Assert.True(await context.Cases.AsNoTracking().AnyAsync(
            item => item.Id == held.Case.Id,
            cancellationToken));
        var tombstone = await context.UserErasures.AsNoTracking().SingleAsync(
            item => item.UserId == userId,
            cancellationToken);
        Assert.Equal(ModerationUserErasureStage.Completed, tombstone.Stage);
        Assert.Equal(1, tombstone.HeldCaseCount);
        Assert.Single(publisher.Erasures);

        var unfencedDelete = await Assert.ThrowsAsync<MySqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM `ModerationAuditEntries` WHERE `CaseId` = {held.Case.Id}",
                cancellationToken));
        Assert.Equal("45000", unfencedDelete.SqlState);

        var persistedHold = await context.LegalHolds.SingleAsync(
            item => item.Id == legalHold.Id,
            cancellationToken);
        persistedHold.Release(
            "legal-admin",
            "hold-released",
            clock.GetUtcNow().AddMinutes(1));
        await context.SaveChangesAsync(cancellationToken);
        clock.UtcNow = clock.UtcNow.AddMinutes(2);
        await DrainErasureAsync(process, cancellationToken);
        context.ChangeTracker.Clear();

        Assert.False(await context.Cases.AsNoTracking().AnyAsync(
            item => item.Id == held.Case.Id,
            cancellationToken));
        Assert.False(await context.Reports.AsNoTracking().AnyAsync(
            item => item.Id == held.Report.Id,
            cancellationToken));
        tombstone = await context.UserErasures.AsNoTracking().SingleAsync(
            item => item.UserId == userId,
            cancellationToken);
        Assert.Equal(0, tombstone.HeldCaseCount);
        Assert.Null(tombstone.NextAttemptAtUtc);
        Assert.Single(publisher.Erasures);
    }

    private static async Task DrainErasureAsync(
        ProcessModerationUserErasureCommand process,
        CancellationToken cancellationToken)
    {
        for (var iteration = 0; iteration < 32; iteration++)
        {
            if (await process.Handle(
                    "moderation-erasure-integration",
                    cancellationToken) == 0)
            {
                return;
            }
        }
        Assert.Fail("Moderation erasure did not drain within the bounded test budget.");
    }

    private static ErasureCaseFixture CreateErasureCase(
        string reporterId,
        DateTimeOffset createdAtUtc,
        char hashCharacter,
        bool includeMessage)
    {
        var reportId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var hash = new string(hashCharacter, 64);
        var resource = new ResourceReference(
            ResourceKind.Comment,
            $"comment_{Guid.NewGuid():N}");
        var report = ModerationReport.Create(
            reportId,
            caseId,
            reporterId,
            resource,
            ReportReason.Spam,
            hash,
            hash,
            createdAtUtc,
            [new EvidenceReferenceInput(
                EvidenceKind.ContentSnapshot,
                $"evidence/{Guid.NewGuid():N}",
                hash)]);
        var moderationCase = ModerationCase.Create(
            caseId,
            reportId,
            resource,
            reporterId,
            createdAtUtc,
            createdAtUtc.AddDays(1));
        ModerationCaseMessage? message = null;
        if (includeMessage)
        {
            message = moderationCase.AddMessage(
                Guid.NewGuid(),
                "investigator",
                $"message/{Guid.NewGuid():N}",
                hash,
                "application/octet-stream",
                "evidence-note",
                moderationCase.Version,
                createdAtUtc.AddMinutes(1));
        }
        return new ErasureCaseFixture(report, moderationCase, message);
    }

    private static RetentionCaseFixture CreateClosedCase(
        DateTimeOffset createdAtUtc,
        bool includeMessage,
        string? reporterId = null)
    {
        var reportId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var resource = new ResourceReference(ResourceKind.Comment, $"comment_{Guid.NewGuid():N}");
        var hash = new string('a', 64);
        var report = ModerationReport.Create(
            reportId,
            caseId,
            reporterId ?? $"reporter-{Guid.NewGuid():N}",
            resource,
            ReportReason.Spam,
            hash,
            hash,
            createdAtUtc,
            [new EvidenceReferenceInput(EvidenceKind.ContentSnapshot, $"evidence/{Guid.NewGuid():N}", hash)]);
        var moderationCase = ModerationCase.Create(
            caseId,
            reportId,
            resource,
            report.ReporterId,
            createdAtUtc,
            createdAtUtc.AddDays(1));
        moderationCase.Triage(
            "triage-actor", "policy-spam", CasePriority.Normal, 1,
            createdAtUtc.AddMinutes(1), createdAtUtc.AddHours(12));
        moderationCase.Assign(
            "triage-actor", "investigator", "skill-match", 2,
            createdAtUtc.AddMinutes(2));
        moderationCase.StartInvestigation(
            "investigator", "review-started", 3,
            createdAtUtc.AddMinutes(3), createdAtUtc.AddHours(10));
        moderationCase.CompleteInvestigation(
            "investigator", "evidence-reviewed", 4,
            createdAtUtc.AddMinutes(4), createdAtUtc.AddHours(8));
        moderationCase.Decide(
            "decision-actor", "policy-confirmed", DecisionAction.RemoveContent, 5,
            createdAtUtc.AddMinutes(5));
        ModerationCaseMessage? message = null;
        if (includeMessage)
        {
            message = moderationCase.AddMessage(
                Guid.NewGuid(), "investigator", $"message/{Guid.NewGuid():N}", hash,
                "application/octet-stream", "evidence-note", 6, createdAtUtc.AddMinutes(6));
        }
        moderationCase.Close(
            "admin-actor",
            "case-complete",
            moderationCase.Version,
            createdAtUtc.AddMinutes(7));
        return new RetentionCaseFixture(report, moderationCase, message);
    }

    private sealed record RetentionCaseFixture(
        ModerationReport Report,
        ModerationCase Case,
        ModerationCaseMessage? Message);

    private sealed record ErasureCaseFixture(
        ModerationReport Report,
        ModerationCase Case,
        ModerationCaseMessage? Message);

    private sealed class ErasureCapturingPublisher : IModerationEventPublisher
    {
        public List<UserErasureCompleted> Erasures { get; } = [];

        public Task PublishDecisionAsync(
            ModerationDecisionMade message,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishActionAsync(
            ModerationActionApplied message,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishEscalationAsync(
            ModerationCaseEscalatedRestricted message,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishCaseChangedAsync(
            ModerationCaseChangedRestricted message,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishErasureCompletedAsync(
            UserErasureCompleted message,
            CancellationToken cancellationToken)
        {
            Erasures.Add(message);
            return Task.CompletedTask;
        }
    }

    private static Task<long> CountAsync(
        ModerationDbContext context,
        string table,
        CancellationToken cancellationToken)
    {
        var sql = table switch
        {
            "OutboxMessage" => "SELECT COUNT(*) AS `Value` FROM `OutboxMessage`",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        return context.Database.SqlQueryRaw<long>(sql).SingleAsync(cancellationToken);
    }
}
