using Interaction.Application.Features.Exports;
using Interaction.Application.Features.Exports.Queries;
using Interaction.Application.Options;
using Interaction.Application.Dtos;
using Interaction.Domain.AggregatesModel;
using Interaction.Infraestructure.Database.Ef;
using Interaction.Infraestructure.Database.Ef.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySqlConnector;
using System.Text.Json;
using Xunit;

namespace Interaction.UnitTests;

[Collection(InteractionMariaDbCollection.Name)]
public sealed class InteractionAuthoritativeExportMariaDbTests
{
    private const string ConnectionEnvironment = "INTERACTION_MARIADB_TEST_CONNECTION";

    public static bool HasIsolatedMariaDb =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironment));

    [Fact(
        Skip = "Requires an explicitly isolated MariaDB database.",
        SkipUnless = nameof(HasIsolatedMariaDb))]
    [Trait("Category", "Integration")]
    public async Task Fixed_authority_boundary_survives_supersession_and_retention_pruning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<InteractionDbContext>()
            .UseMySql(
                Environment.GetEnvironmentVariable(ConnectionEnvironment)!,
                new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options;
        await using var context = new InteractionDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow.AddDays(-30);
        var firstResource = new ResourceRef(ResourceType.Comment, $"authority-{Guid.NewGuid():N}");
        var lastResource = new ResourceRef(ResourceType.Comment, $"authority-{Guid.NewGuid():N}");
        AddResource(context, firstResource, now, 1);
        AddResource(context, lastResource, now, 2);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        var repository = new InteractionRepository(context);
        var clock = TimeProvider.System;
        var codec = new InteractionAuthoritativeExportCursor(
            Options.Create(new AuthoritativeExportOptions
            {
                CursorSigningKey = Convert.ToBase64String(
                    Enumerable.Repeat((byte)0x52, 32).ToArray()),
            }),
            clock);
        var query = new ExportInteractionAuthoritativeStateQuery(repository, codec, clock);
        var firstPage = await query.Handle(
            new ExportInteractionAuthoritativeStateRequest(null, 1),
            cancellationToken);
        Assert.True(firstPage.HasMore);
        var fixedHighWatermark = firstPage.Snapshot.HighWatermark;
        var protectedRevisionCursor = await context.AuthorityRevisions
            .Where(revision => revision.ResourceType == lastResource.Type
                && revision.ResourceId == lastResource.Id)
            .MaxAsync(revision => revision.RevisionCursor, cancellationToken);

        var laterResource = new ResourceRef(ResourceType.Comment, $"authority-{Guid.NewGuid():N}");
        AddResource(context, laterResource, now.AddSeconds(1), 3);
        var lastSummary = await context.EngagementSummaries.SingleAsync(
            item => item.ResourceType == lastResource.Type && item.ResourceId == lastResource.Id,
            cancellationToken);
        lastSummary.ApplyViews(7, now.AddSeconds(1));
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        await repository.PruneSupersededAsync(
            DateTimeOffset.UtcNow.AddDays(-7),
            5000,
            cancellationToken);
        Assert.True(await context.AuthorityRevisions.AnyAsync(
            revision => revision.RevisionCursor == protectedRevisionCursor,
            cancellationToken));

        var laterCursor = await context.AuthorityCoordinates
            .Where(item => item.ResourceType == laterResource.Type && item.ResourceId == laterResource.Id)
            .Select(item => item.Cursor)
            .SingleAsync(cancellationToken);
        Assert.True(laterCursor > fixedHighWatermark);

        var pages = new List<InteractionAuthoritativeExportPageDto> { firstPage };
        var cursor = firstPage.NextCursor;
        while (cursor is not null)
        {
            var page = await query.Handle(
                new ExportInteractionAuthoritativeStateRequest(cursor, null),
                cancellationToken);
            pages.Add(page);
            cursor = page.NextCursor;
        }

        var items = pages.SelectMany(page => page.Items).ToArray();
        AssertSnapshotIntegrity(firstPage.Snapshot, pages);
        Assert.Equal(items.Length, items.Select(item => item.Cursor).Distinct().Count());
        Assert.All(items, item => Assert.True(item.Cursor <= fixedHighWatermark));
        Assert.DoesNotContain(items, item => item.ResourceId == laterResource.Id);
        Assert.Contains(items, item => item.ResourceId == firstResource.Id);
        var updated = Assert.Single(items, item => item.ResourceId == lastResource.Id);
        Assert.Equal(2, updated.EngagementSummary!.ViewCount);
        Assert.All(pages, page => Assert.Equal(firstPage.Snapshot, page.Snapshot));

        var freshPages = await ReadAllFreshPagesAsync(query, cancellationToken);
        var fresh = freshPages[0];
        var freshItems = freshPages.SelectMany(page => page.Items).ToArray();
        AssertSnapshotIntegrity(fresh.Snapshot, freshPages);
        Assert.Equal(
            9,
            Assert.Single(freshItems, item => item.ResourceId == lastResource.Id)
                .EngagementSummary!.ViewCount);
        Assert.NotEqual(firstPage.Snapshot.GlobalXorSha256, fresh.Snapshot.GlobalXorSha256);
        Assert.True(fresh.Snapshot.RevisionHighWatermark > firstPage.Snapshot.RevisionHighWatermark);

        var targetRevisions = await context.AuthorityRevisions
            .Where(revision => revision.ResourceType == lastResource.Type
                && revision.ResourceId == lastResource.Id)
            .OrderBy(revision => revision.RevisionCursor)
            .Select(revision => revision.RevisionCursor)
            .ToArrayAsync(cancellationToken);
        Assert.True(targetRevisions.Length > 1);
        var updateRejected = await Assert.ThrowsAsync<MySqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE `InteractionAuthoritativeStateRevisions` SET `RowSha256` = `RowSha256` WHERE `RevisionCursor` = {targetRevisions[0]}",
                cancellationToken));
        Assert.Equal("45000", updateRejected.SqlState);
        var deleteRejected = await Assert.ThrowsAsync<MySqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM `InteractionAuthoritativeStateRevisions` WHERE `RevisionCursor` = {targetRevisions[0]}",
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
            await context.AuthorityRevisions.CountAsync(
                revision => revision.ResourceType == lastResource.Type
                    && revision.ResourceId == lastResource.Id,
                cancellationToken));
        var remainingCursor = await context.AuthorityRevisions
            .Where(revision => revision.ResourceType == lastResource.Type
                && revision.ResourceId == lastResource.Id)
            .Select(revision => revision.RevisionCursor)
            .SingleAsync(cancellationToken);
        var guardReset = await Assert.ThrowsAsync<MySqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM `InteractionAuthoritativeStateRevisions` WHERE `RevisionCursor` = {remainingCursor}",
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
        var options = new DbContextOptionsBuilder<InteractionDbContext>()
            .UseMySql(
                Environment.GetEnvironmentVariable(ConnectionEnvironment)!,
                new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options;
        await using var context = new InteractionDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);
        if (!await context.AuthorityCoordinates.AnyAsync(cancellationToken))
        {
            AddResource(
                context,
                new ResourceRef(ResourceType.Comment, $"explain-{Guid.NewGuid():N}"),
                DateTimeOffset.UtcNow,
                1);
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
        }
        var repository = new InteractionRepository(context);
        var snapshot = await repository.CaptureSnapshotAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN FORMAT=JSON\n" + InteractionRepository.AuthoritativePageSql
            .Replace("@revisionHighWatermark", snapshot.RevisionHighWatermark.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("@afterCursor", "0", StringComparison.Ordinal)
            .Replace("@highWatermark", snapshot.HighWatermark.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("@take", (AuthoritativeExportOptions.MaximumPageSize + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var plan = Assert.IsType<string>(await command.ExecuteScalarAsync(cancellationToken));

        Assert.Contains("IX_InteractionAuthorityRevision_Resource_Cursor", plan, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(plan);
        Assert.True(HasPlanProperty(
            json.RootElement,
            "key",
            "IX_InteractionAuthorityRevision_Resource_Cursor"));
        var compactPlan = string.Concat(plan.Where(character => !char.IsWhiteSpace(character)));
        Assert.Contains("\"table_name\":\"c\",\"access_type\":\"range\"", compactPlan, StringComparison.Ordinal);
        Assert.Contains("\"table_name\":\"r\",\"access_type\":\"eq_ref\"", compactPlan, StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<InteractionAuthoritativeExportPageDto>>
        ReadAllFreshPagesAsync(
            ExportInteractionAuthoritativeStateQuery query,
            CancellationToken cancellationToken)
    {
        var pages = new List<InteractionAuthoritativeExportPageDto>();
        string? cursor = null;
        do
        {
            var page = await query.Handle(
                new ExportInteractionAuthoritativeStateRequest(
                    cursor,
                    cursor is null ? AuthoritativeExportOptions.MaximumPageSize : null),
                cancellationToken);
            pages.Add(page);
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return pages;
    }

    private static void AssertSnapshotIntegrity(
        InteractionAuthoritativeExportSnapshotDto snapshot,
        IReadOnlyList<InteractionAuthoritativeExportPageDto> pages)
    {
        var items = pages.SelectMany(page => page.Items).ToArray();
        Assert.Equal(snapshot.ExpectedItemCount, items.LongLength);
        Assert.Equal(snapshot.GlobalXorSha256, Xor(items.Select(item => item.RowSha256)));
        Assert.Equal(
            new ExportVersionRangeDto(
                items.Length == 0 ? null : items.Min(item => item.ResourceState.AppliedVersion),
                items.Length == 0 ? null : items.Max(item => item.ResourceState.AppliedVersion)),
            snapshot.ResourceVersionRange);
        var summaries = items
            .Where(item => item.EngagementSummary is not null)
            .Select(item => item.EngagementSummary!)
            .ToArray();
        Assert.Equal(
            new ExportVersionRangeDto(
                summaries.Length == 0 ? null : summaries.Min(item => item.Version),
                summaries.Length == 0 ? null : summaries.Max(item => item.Version)),
            snapshot.SummaryVersionRange);
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


    private static void AddResource(
        InteractionDbContext context,
        ResourceRef resource,
        DateTimeOffset now,
        long views)
    {
        var state = new ResourceState(resource, now);
        state.ApplyBaseline(ResourceLifecycle.Active, ResourceVisibility.Public, 1, now);
        var summary = new EngagementSummary(resource, now);
        summary.ApplyViews(views, now);
        context.AddRange(state, summary);
    }
}
