using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moderation.Application.Dtos;
using Moderation.Application.Features.Exports;
using Moderation.Application.Features.Exports.Queries;
using Moderation.Application.Options;
using Moderation.Domain.AggregatesModel;
using Moderation.Infra.Database.Ef;
using Moderation.Infra.Database.Ef.Repositories;
using MySqlConnector;
using System.Text.Json;

namespace Moderation.UnitTests;

[Collection(ModerationMariaDbCollection.Name)]
public sealed class ModerationDecisionExportMariaDbTests
{
    private const string ConnectionEnvironment = "MODERATION_MARIADB_TEST_CONNECTION";

    public static bool HasIsolatedMariaDb =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironment));

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    [Trait("Category", "Integration")]
    public async Task Fixed_decision_boundary_survives_supersession_and_retention_pruning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ModerationDbContext>()
            .UseMySql(
                Environment.GetEnvironmentVariable(ConnectionEnvironment)!,
                new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options;
        await using var context = new ModerationDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow.AddDays(-30);
        var firstDecision = CreateDecidedCase(now, "first");
        var updatedDecision = CreateDecidedCase(now.AddMilliseconds(1), "updated");
        context.AddRange(
            firstDecision.Report,
            firstDecision.Case,
            updatedDecision.Report,
            updatedDecision.Case);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        var repository = new ModerationRepository(context);
        var clock = TimeProvider.System;
        var codec = new ModerationDecisionExportCursor(
            Options.Create(new ModerationDecisionExportOptions
            {
                CursorSigningKey = Convert.ToBase64String(
                    Enumerable.Repeat((byte)0x67, 32).ToArray()),
            }),
            clock);
        var query = new ExportModerationDecisionStateQuery(repository, codec, clock);
        var firstPage = (await query.HandleAsync(null, 1, cancellationToken)).Value!;
        Assert.True(firstPage.HasMore);
        var fixedHighWatermark = firstPage.Snapshot.HighWatermark;
        var protectedRevisionCursor = await context.DecisionStateRevisions
            .Where(revision => revision.CaseId == updatedDecision.Case.Id)
            .Select(revision => revision.RevisionCursor)
            .SingleAsync(cancellationToken);

        var laterDecision = CreateDecidedCase(now.AddSeconds(1), "later");
        context.AddRange(laterDecision.Report, laterDecision.Case);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        var mutable = await repository.GetCaseAsync(updatedDecision.Case.Id, cancellationToken);
        Assert.NotNull(mutable);
        mutable!.Appeal(
            updatedDecision.Report.ReporterId,
            "appeal-requested",
            mutable.Version,
            now.AddSeconds(2),
            now.AddHours(2));
        mutable.StartInvestigation(
            "investigator",
            "appeal-review",
            mutable.Version,
            now.AddSeconds(3),
            now.AddHours(3));
        mutable.CompleteInvestigation(
            "investigator",
            "appeal-complete",
            mutable.Version,
            now.AddSeconds(4),
            now.AddHours(4));
        mutable.Decide(
            "decision-actor",
            "appeal-upheld",
            DecisionAction.NoAction,
            mutable.Version,
            now.AddSeconds(5));
        mutable.Close(
            "admin-actor",
            "case-complete",
            mutable.Version,
            now.AddSeconds(6));
        await repository.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        await repository.PruneSupersededAsync(
            DateTimeOffset.UtcNow.AddDays(-7),
            5000,
            cancellationToken);
        Assert.True(await context.DecisionStateRevisions.AnyAsync(
            revision => revision.RevisionCursor == protectedRevisionCursor,
            cancellationToken));

        var retentionNow = DateTimeOffset.UtcNow;
        var purge = await new ModerationRetentionStore(
            context,
            ModerationTestFactory.CreateSubjectPseudonymizer()).ExecuteBatchAsync(
            retentionNow,
            retentionNow,
            retentionNow,
            $"export-test-{Guid.NewGuid():N}",
            TimeSpan.FromMinutes(2),
            500,
            cancellationToken);
        Assert.True(purge.LeaseAcquired);
        Assert.True(purge.CasesPurged > 0);
        Assert.False(await context.Cases.AnyAsync(
            moderationCase => moderationCase.Id == updatedDecision.Case.Id,
            cancellationToken));
        Assert.False(await context.DecisionStates.AnyAsync(
            state => state.CaseId == updatedDecision.Case.Id,
            cancellationToken));

        var laterCursor = await context.DecisionStates
            .Where(state => state.CaseId == laterDecision.Case.Id)
            .Select(state => state.ExportCursor)
            .SingleAsync(cancellationToken);
        Assert.True(laterCursor > fixedHighWatermark);

        var pages = new List<ModerationDecisionExportPageDto> { firstPage };
        var cursor = firstPage.NextCursor;
        while (cursor is not null)
        {
            var pageResult = await query.HandleAsync(cursor, null, cancellationToken);
            Assert.True(pageResult.IsSuccess);
            var page = pageResult.Value!;
            pages.Add(page);
            cursor = page.NextCursor;
        }

        var items = pages.SelectMany(page => page.Items).ToArray();
        AssertSnapshotIntegrity(firstPage.Snapshot, pages);
        Assert.Equal(items.Length, items.Select(item => item.Cursor).Distinct().Count());
        Assert.All(items, item => Assert.True(item.Cursor <= fixedHighWatermark));
        Assert.DoesNotContain(items, item => item.CaseId == laterDecision.Case.Id);
        Assert.Contains(items, item => item.CaseId == firstDecision.Case.Id);
        var updated = Assert.Single(items, item => item.CaseId == updatedDecision.Case.Id);
        Assert.Equal("removecontent", updated.Action);
        Assert.Equal(1, updated.DecisionVersion);
        Assert.All(pages, page => Assert.Equal(firstPage.Snapshot, page.Snapshot));

        var freshPages = await ReadAllFreshPagesAsync(query, cancellationToken);
        var fresh = freshPages[0];
        var freshItems = freshPages.SelectMany(page => page.Items).ToArray();
        AssertSnapshotIntegrity(fresh.Snapshot, freshPages);
        var freshUpdated = Assert.Single(
            freshItems,
            item => item.CaseId == updatedDecision.Case.Id);
        Assert.Equal("noaction", freshUpdated.Action);
        Assert.Equal(2, freshUpdated.DecisionVersion);
        Assert.NotEqual(firstPage.Snapshot.GlobalXorSha256, fresh.Snapshot.GlobalXorSha256);
        Assert.True(fresh.Snapshot.RevisionHighWatermark > firstPage.Snapshot.RevisionHighWatermark);

        var targetRevisions = await context.DecisionStateRevisions
            .Where(revision => revision.CaseId == updatedDecision.Case.Id)
            .OrderBy(revision => revision.RevisionCursor)
            .Select(revision => revision.RevisionCursor)
            .ToArrayAsync(cancellationToken);
        Assert.True(targetRevisions.Length > 1);
        var updateRejected = await Assert.ThrowsAsync<MySqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE `ModerationDecisionStateRevisions` SET `RowSha256` = `RowSha256` WHERE `RevisionCursor` = {targetRevisions[0]}",
                cancellationToken));
        Assert.Equal("45000", updateRejected.SqlState);
        var deleteRejected = await Assert.ThrowsAsync<MySqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM `ModerationDecisionStateRevisions` WHERE `RevisionCursor` = {targetRevisions[0]}",
                cancellationToken));
        Assert.Equal("45000", deleteRejected.SqlState);

        var activeWindow = await repository.GetRetentionStateAsync(
            DateTimeOffset.UtcNow.AddDays(-7),
            500,
            cancellationToken);
        Assert.Equal(0, activeWindow.EligibleRevisionCountLowerBound);
        Assert.Equal(0, await repository.PruneSupersededAsync(
            DateTimeOffset.UtcNow.AddDays(-7),
            500,
            cancellationToken));
        Assert.True(await repository.PruneSupersededAsync(
            DateTimeOffset.UtcNow.AddDays(8),
            5000,
            cancellationToken) > 0);
        Assert.Equal(
            1,
            await context.DecisionStateRevisions.CountAsync(
                revision => revision.CaseId == updatedDecision.Case.Id,
                cancellationToken));
        var remainingCursor = await context.DecisionStateRevisions
            .Where(revision => revision.CaseId == updatedDecision.Case.Id)
            .Select(revision => revision.RevisionCursor)
            .SingleAsync(cancellationToken);
        var guardReset = await Assert.ThrowsAsync<MySqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM `ModerationDecisionStateRevisions` WHERE `RevisionCursor` = {remainingCursor}",
                cancellationToken));
        Assert.Equal("45000", guardReset.SqlState);
    }

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    [Trait("Category", "Integration")]
    public async Task Export_page_query_plan_uses_bounded_coordinate_and_latest_revision_indexes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ModerationDbContext>()
            .UseMySql(
                Environment.GetEnvironmentVariable(ConnectionEnvironment)!,
                new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options;
        await using var context = new ModerationDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);
        if (!await context.DecisionStateRevisions.AnyAsync(cancellationToken))
        {
            var fixture = CreateDecidedCase(DateTimeOffset.UtcNow, "explain");
            context.AddRange(fixture.Report, fixture.Case);
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
        }
        var repository = new ModerationRepository(context);
        var snapshot = await repository.CaptureSnapshotAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN FORMAT=JSON\n" + ModerationRepository.DecisionAuthorityPageSql
            .Replace("@revisionHighWatermark", snapshot.RevisionHighWatermark.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("@afterCursor", "0", StringComparison.Ordinal)
            .Replace("@highWatermark", snapshot.HighWatermark.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("@take", (ModerationDecisionExportOptions.MaximumPageSize + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var plan = Assert.IsType<string>(await command.ExecuteScalarAsync(cancellationToken));

        Assert.Contains("IX_ModerationDecisionRevision_Case_Cursor", plan, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(plan);
        Assert.True(HasPlanProperty(
            json.RootElement,
            "key",
            "IX_ModerationDecisionRevision_Case_Cursor"));
        var compactPlan = string.Concat(plan.Where(character => !char.IsWhiteSpace(character)));
        Assert.Contains("\"table_name\":\"c\",\"access_type\":\"range\"", compactPlan, StringComparison.Ordinal);
        Assert.Contains("\"table_name\":\"r\",\"access_type\":\"eq_ref\"", compactPlan, StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<ModerationDecisionExportPageDto>>
        ReadAllFreshPagesAsync(
            ExportModerationDecisionStateQuery query,
            CancellationToken cancellationToken)
    {
        var pages = new List<ModerationDecisionExportPageDto>();
        string? cursor = null;
        do
        {
            var result = await query.HandleAsync(
                cursor,
                cursor is null ? ModerationDecisionExportOptions.MaximumPageSize : null,
                cancellationToken);
            Assert.True(result.IsSuccess);
            var page = result.Value!;
            pages.Add(page);
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return pages;
    }

    private static void AssertSnapshotIntegrity(
        ModerationDecisionExportSnapshotDto snapshot,
        IReadOnlyList<ModerationDecisionExportPageDto> pages)
    {
        var items = pages.SelectMany(page => page.Items).ToArray();
        Assert.Equal(snapshot.ExpectedItemCount, items.LongLength);
        Assert.Equal(snapshot.GlobalXorSha256, Xor(items.Select(item => item.RowSha256)));
        Assert.Equal(
            new ModerationExportVersionRangeDto(
                items.Length == 0 ? null : items.Min(item => item.DecisionVersion),
                items.Length == 0 ? null : items.Max(item => item.DecisionVersion)),
            snapshot.DecisionVersionRange);
        Assert.Equal(
            new ModerationExportVersionRangeDto(
                items.Length == 0 ? null : items.Min(item => item.CaseVersion),
                items.Length == 0 ? null : items.Max(item => item.CaseVersion)),
            snapshot.CaseVersionRange);
        Assert.All(pages, page =>
        {
            Assert.Equal(page.Items.Count, page.Integrity.ItemCount);
            Assert.Equal(Xor(page.Items.Select(item => item.RowSha256)), Xor(page.Integrity.Partitions.Select(item => item.XorSha256)));
        });
    }

    private static string Xor(IEnumerable<string> hashes)
    {
        var result = new byte[32];
        foreach (var hash in hashes)
        {
            var bytes = Convert.FromHexString(hash);
            for (var index = 0; index < result.Length; index++)
                result[index] ^= bytes[index];
        }
        return Convert.ToHexStringLower(result);
    }

    private static bool HasPlanProperty(JsonElement element, string name, string value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.String
                && property.GetString() == value)
                return true;
            foreach (var child in element.EnumerateObject())
            {
                if (HasPlanProperty(child.Value, name, value))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                if (HasPlanProperty(child, name, value))
                    return true;
            }
        }
        return false;
    }


    private static DecisionFixture CreateDecidedCase(DateTimeOffset createdAtUtc, string marker)
    {
        var reportId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var resource = new ResourceReference(
            ResourceKind.Comment,
            $"authority-{marker}-{Guid.NewGuid():N}");
        var hash = new string('b', 64);
        var reporter = $"reporter-{Guid.NewGuid():N}";
        var report = ModerationReport.Create(
            reportId,
            caseId,
            reporter,
            resource,
            ReportReason.Spam,
            hash,
            hash,
            createdAtUtc,
            []);
        var moderationCase = ModerationCase.Create(
            caseId,
            reportId,
            resource,
            reporter,
            createdAtUtc,
            createdAtUtc.AddHours(1));
        moderationCase.Triage(
            "triage-actor", "policy-spam", CasePriority.Normal,
            moderationCase.Version, createdAtUtc.AddMinutes(1), createdAtUtc.AddHours(2));
        moderationCase.Assign(
            "triage-actor", "investigator", "skill-match",
            moderationCase.Version, createdAtUtc.AddMinutes(2));
        moderationCase.StartInvestigation(
            "investigator", "review-started", moderationCase.Version,
            createdAtUtc.AddMinutes(3), createdAtUtc.AddHours(3));
        moderationCase.CompleteInvestigation(
            "investigator", "review-complete", moderationCase.Version,
            createdAtUtc.AddMinutes(4), createdAtUtc.AddHours(4));
        moderationCase.Decide(
            "decision-actor", "policy-confirmed", DecisionAction.RemoveContent,
            moderationCase.Version, createdAtUtc.AddMinutes(5));
        return new DecisionFixture(report, moderationCase);
    }

    private sealed record DecisionFixture(ModerationReport Report, ModerationCase Case);
}
