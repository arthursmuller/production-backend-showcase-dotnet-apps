using Feed.Application.Features.Feeds;
using System.Text;
using Xunit;

namespace Feed.UnitTests;

public sealed class FeedSecurityBudgetTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 17, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Signed_cursor_is_deterministic_and_rejects_unsigned_tampered_or_rescoped_tokens()
    {
        var options = Keys(("primary", 0x11));
        var cursor = Cursor();
        var encoded = Encode(cursor, options);

        Assert.Equal(encoded, Encode(cursor, options));
        Assert.Equal(cursor, Decode(encoded, options));
        Assert.StartsWith("primary.", encoded, StringComparison.Ordinal);
        Assert.True(encoded.Length <= 512);

        var segments = encoded.Split('.');
        segments[2] = (segments[2][0] == 'A' ? "B" : "A") + segments[2][1..];
        var tampered = string.Join('.', segments);
        Assert.Throws<FeedCursorRejectedException>(() => Decode(tampered, options));
        Assert.Throws<FeedCursorRejectedException>(() => Decode(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("unsigned-cursor")), options));
        Assert.Throws<FeedCursorRejectedException>(() => Decode(encoded, options, viewerUserId: 18));
        Assert.Throws<FeedCursorRejectedException>(() => Decode(encoded, options, revision: 10));
        Assert.Throws<FeedCursorRejectedException>(() => Decode(encoded, options, pageSize: 21));
        Assert.Throws<FeedCursorRejectedException>(() => Decode(encoded, options, includeSeen: false));
        Assert.Throws<FeedCursorRejectedException>(() => Decode(encoded, options, surface: "unseen"));
    }

    [Fact]
    public void Cursor_expires_and_key_rotation_accepts_only_configured_validation_keys()
    {
        var oldOnly = Keys(("old", 0x21));
        oldOnly.LifetimeSeconds = 60;
        var encoded = Encode(Cursor(), oldOnly);
        Assert.Throws<FeedCursorRejectedException>(() => Decode(
            encoded,
            oldOnly,
            now: Now.AddSeconds(oldOnly.LifetimeSeconds + oldOnly.ClockSkewSeconds + 1)));

        var overlap = Keys(("new", 0x31), ("old", 0x21));
        Assert.Equal(Cursor(), Decode(encoded, overlap));
        var newOnly = Keys(("new", 0x31));
        Assert.Throws<FeedCursorRejectedException>(() => Decode(encoded, newOnly));

        var wrongOldKey = Keys(("old", 0x22));
        Assert.Throws<FeedCursorRejectedException>(() => Decode(encoded, wrongOldKey));
        Assert.False(FeedCursorOptionsValidation.IsValid(new FeedCursorOptions
        {
            ActiveKeyId = "weak",
            SigningKeys = new Dictionary<string, string>
            {
                ["weak"] = Convert.ToBase64String(new byte[31]),
            },
        }));
    }

    [Fact]
    public void Cursor_allows_bounded_cross_replica_clock_skew_but_rejects_larger_snapshot_drift()
    {
        var options = Keys(("primary", 0x41));
        var withinSkew = CursorAt(Now.AddSeconds(options.ClockSkewSeconds - 1));
        var withinSkewToken = FeedCursorCodec.Encode(
            withinSkew,
            17,
            9,
            20,
            true,
            FeedCursorSurfaces.Items,
            options,
            Now.AddSeconds(options.ClockSkewSeconds - 1),
            512);
        Assert.Equal(withinSkew, Decode(withinSkewToken, options, now: Now));

        var beyondSkew = CursorAt(Now.AddSeconds(options.ClockSkewSeconds + 1));
        var beyondSkewToken = FeedCursorCodec.Encode(
            beyondSkew,
            17,
            9,
            20,
            true,
            FeedCursorSurfaces.Items,
            options,
            Now,
            512);
        Assert.Throws<FeedCursorRejectedException>(() =>
            Decode(beyondSkewToken, options, now: Now));
    }

    [Fact]
    public void Pull_fairness_allocates_every_publisher_without_skipping_reverse_chronological_rows()
    {
        var rows = Enumerable.Range(1, 9)
            .Select(rank => new FairRow(1, 100 - rank))
            .Concat([
                new FairRow(2, 50),
                new FairRow(3, 40),
                new FairRow(4, 30),
            ])
            .ToArray();

        var selected = FeedReadPolicy.SelectPerPublisher(
            rows,
            row => row.PublisherId,
            perPublisherLimit: 2,
            totalLimit: 8);

        Assert.True(selected.Truncated);
        Assert.Equal(5, selected.Items.Count);
        Assert.Equal(2, selected.Items.Count(row => row.PublisherId == 1));
        Assert.Contains(selected.Items, row => row.PublisherId == 2);
        Assert.Contains(selected.Items, row => row.PublisherId == 3);
        Assert.Contains(selected.Items, row => row.PublisherId == 4);
        var excludedFrontier = selected.ExcludedItems
            .Select(row => Tuple(row.Rank))
            .OrderByDescending(tuple => tuple)
            .First();
        Assert.True(FeedReadPolicy.IsNewerThanExcludedFrontier(
            Tuple(selected.Items[0].Rank), excludedFrontier));
        Assert.False(FeedReadPolicy.IsNewerThanExcludedFrontier(
            Tuple(Assert.Single(selected.Items, row => row.PublisherId == 2).Rank),
            excludedFrontier));
    }

    [Fact]
    public void Row_read_merge_and_response_breaches_fail_the_complete_request()
    {
        var time = new ManualTimeProvider(Now);
        var options = BudgetOptions();

        var materialized = new FeedReadWorkBudget(options, time);
        materialized.Materialized(2);
        AssertReason(
            FeedReadBudgetReasons.MaterializedRows,
            Assert.Throws<FeedReadBudgetExceededException>(() => materialized.Materialized(1)));

        var processed = new FeedReadWorkBudget(options, time);
        processed.Processed(3);
        AssertReason(
            FeedReadBudgetReasons.ProcessedRows,
            Assert.Throws<FeedReadBudgetExceededException>(() => processed.Processed(1)));

        var read = new FeedReadWorkBudget(options, time);
        time.Advance(TimeSpan.FromMilliseconds(101));
        AssertReason(
            FeedReadBudgetReasons.ReadDeadline,
            Assert.Throws<FeedReadBudgetExceededException>(read.CheckReadDeadline));

        time = new ManualTimeProvider(Now);
        var merge = new FeedReadWorkBudget(options, time);
        var mergeException = Assert.Throws<FeedReadBudgetExceededException>(() =>
        {
            using (merge.MeasureMerge())
                time.Advance(TimeSpan.FromMilliseconds(51));
        });
        AssertReason(FeedReadBudgetReasons.MergeDeadline, mergeException);

        var bytes = new FeedReadWorkBudget(options, new ManualTimeProvider(Now));
        var oversized = Page(new string('x', 5_000));
        AssertReason(
            FeedReadBudgetReasons.ResponseBytes,
            Assert.Throws<FeedReadBudgetExceededException>(() => bytes.EnsureResponseFits(oversized)));
    }

    [Fact]
    public void Normal_budget_and_cache_zero_policy_are_stable()
    {
        var options = WithBudgetHeadroom(BudgetOptions());
        var budget = new FeedReadWorkBudget(options, new ManualTimeProvider(Now));
        budget.Materialized(10);
        budget.Processed(20);
        using (budget.MeasureMerge())
        {
        }
        budget.EnsureResponseFits(Page("normal"));

        Assert.True(FeedReadPolicy.ShouldUseFirstPageCache(
            new FeedOptions { EnableFirstPageCache = true }, false, 0));
        Assert.False(FeedReadPolicy.ShouldUseFirstPageCache(
            new FeedOptions { EnableFirstPageCache = false }, false, 0));
        Assert.False(FeedReadPolicy.ShouldUseFirstPageCache(
            new FeedOptions { EnableFirstPageCache = true }, true, 0));
        Assert.False(FeedReadPolicy.ShouldUseFirstPageCache(
            new FeedOptions { EnableFirstPageCache = true }, false, 1));
    }

    [Fact]
    public void Production_options_validate_every_hard_read_cap()
    {
        Assert.True(FeedOptionsValidation.IsValid(new FeedOptions()));
        Assert.False(FeedOptionsValidation.IsValid(new FeedOptions
        {
            MaximumCandidatesPerPullPublisher = 65,
        }));
        Assert.False(FeedOptionsValidation.IsValid(new FeedOptions
        {
            MaximumMaterializedRows = 8_193,
        }));
        Assert.False(FeedOptionsValidation.IsValid(new FeedOptions
        {
            MaximumProcessedRows = 32_769,
        }));
        Assert.False(FeedOptionsValidation.IsValid(new FeedOptions
        {
            ReadDeadlineMilliseconds = 5_001,
        }));
        Assert.False(FeedOptionsValidation.IsValid(new FeedOptions
        {
            MergeDeadlineMilliseconds = 1_001,
        }));
        Assert.False(FeedOptionsValidation.IsValid(new FeedOptions
        {
            MaximumSerializedResponseBytes = 1_048_577,
        }));
    }

    private static FeedCursor Cursor() => new(
        Now.UtcDateTime.Ticks,
        new FeedSortTuple(300, Now.UtcDateTime.Ticks, 90),
        new FeedSortTuple(200, Now.AddSeconds(-1).UtcDateTime.Ticks, 80));

    private static FeedCursor CursorAt(DateTimeOffset snapshot) => new(
        snapshot.UtcDateTime.Ticks,
        new FeedSortTuple(300, snapshot.UtcDateTime.Ticks, 90),
        new FeedSortTuple(200, snapshot.AddSeconds(-1).UtcDateTime.Ticks, 80));

    private static FeedSortTuple Tuple(int rank) =>
        new(rank, Now.UtcDateTime.Ticks, rank);

    private static string Encode(FeedCursor cursor, FeedCursorOptions options) =>
        FeedCursorCodec.Encode(
            cursor,
            viewerUserId: 17,
            feedRevision: 9,
            pageSize: 20,
            includeSeen: true,
            FeedCursorSurfaces.Items,
            options,
            Now,
            maximumLength: 512);

    private static FeedCursor Decode(
        string encoded,
        FeedCursorOptions options,
        int viewerUserId = 17,
        long revision = 9,
        int pageSize = 20,
        bool includeSeen = true,
        string surface = FeedCursorSurfaces.Items,
        DateTimeOffset? now = null) =>
        FeedCursorCodec.Decode(
            encoded,
            viewerUserId,
            revision,
            pageSize,
            includeSeen,
            surface,
            options,
            now ?? Now,
            maximumLength: 512);

    private static FeedCursorOptions Keys(params (string Id, byte Value)[] keys) => new()
    {
        ActiveKeyId = keys[0].Id,
        SigningKeys = keys.ToDictionary(
            key => key.Id,
            key => Convert.ToBase64String(Enumerable.Repeat(key.Value, 32).ToArray()),
            StringComparer.Ordinal),
        LifetimeSeconds = 60,
        ClockSkewSeconds = 15,
    };

    private static FeedOptions BudgetOptions() => new()
    {
        MaximumMaterializedRows = 2,
        MaximumProcessedRows = 3,
        ReadDeadlineMilliseconds = 100,
        MergeDeadlineMilliseconds = 50,
        MaximumSerializedResponseBytes = 256,
    };

    private static FeedOptions WithBudgetHeadroom(FeedOptions options)
    {
        options.MaximumMaterializedRows = 100;
        options.MaximumProcessedRows = 100;
        options.ReadDeadlineMilliseconds = 1_000;
        options.MergeDeadlineMilliseconds = 100;
        options.MaximumSerializedResponseBytes = 4_096;
        return options;
    }

    private static FeedPage Page(string summary) => new(
        [new FeedItem(
            1,
            2,
            "post",
            "3",
            "created",
            4,
            "public",
            summary,
            5,
            Now.UtcDateTime,
            null,
            "push")],
        null,
        false,
        false,
        1);

    private static void AssertReason(string expected, FeedReadBudgetExceededException exception) =>
        Assert.Equal(expected, exception.Reason);

    private sealed record FairRow(int PublisherId, int Rank);

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private long _timestamp;
        private DateTimeOffset _utcNow = utcNow;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan elapsed)
        {
            _utcNow += elapsed;
            _timestamp += elapsed.Ticks;
        }
    }
}
