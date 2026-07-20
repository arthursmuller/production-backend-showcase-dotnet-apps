using Comment.Application.Features.Media.Commands;
using Comment.Application.Features.Privacy;
using Comment.Application.Options;
using Comment.Application.Providers.Storage;
using Comment.Domain.AggregatesModel;
using Comment.Domain.AggregatesModel.Media;
using Comment.Domain.Exceptions;
using Comment.Domain.Repositories;
using StarterEntities.Storage;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Xunit;

namespace Comment.UnitTests;

public sealed class CommentMediaIngestionTests
{
    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
    ];

    [Fact]
    public async Task Pending_record_and_exclusive_claim_precede_first_object_side_effect()
    {
        var fixture = Fixture();
        fixture.Objects.BeforePut = () =>
        {
            Assert.NotNull(fixture.Repository.Ingestion);
            Assert.Equal(CommentMediaIngestionState.Pending, fixture.Repository.Ingestion!.State);
            Assert.NotNull(fixture.Repository.Ingestion.LeaseOwner);
            Assert.True(fixture.Repository.SaveCount >= 2);
        };

        var result = await fixture.Service.IngestAsync(
            [File()], 42, "stable-upload-0001", "create", null,
            TestContext.Current.CancellationToken);

        Assert.Equal(CommentMediaIngestionState.Promoted, result.Ingestion.State);
        Assert.Single(result.Media);
        Assert.Equal(
            result.Ingestion.Items[0].FinalObjectName,
            result.Media[0].Url);
        Assert.DoesNotContain("http", result.Media[0].Url, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ClamAV", result.Ingestion.Items[0].Scanner);
        Assert.Equal("27422", result.Ingestion.Items[0].SignatureVersion);
        Assert.Empty(fixture.Objects.Quarantine);
        Assert.Single(fixture.Objects.Final);
    }

    [Fact]
    public async Task Duplicate_retry_reuses_promoted_identity_without_another_write()
    {
        var fixture = Fixture();
        var first = await fixture.Service.IngestAsync(
            [File()], 42, "stable-upload-0002", "create", null,
            TestContext.Current.CancellationToken);
        var second = await fixture.Service.IngestAsync(
            [File()], 42, "stable-upload-0002", "create", null,
            TestContext.Current.CancellationToken);

        Assert.False(first.IsReplay);
        Assert.True(second.IsReplay);
        Assert.Equal(first.Ingestion.UploadIdentity, second.Ingestion.UploadIdentity);
        Assert.Equal(1, fixture.Objects.PutCalls);
        Assert.Equal(1, fixture.Objects.PromoteCalls);
    }

    [Fact]
    public async Task Concurrent_same_identity_cannot_overwrite_quarantine_object()
    {
        var fixture = Fixture();
        fixture.Objects.PauseFirstPut = true;
        var first = fixture.Service.IngestAsync(
            [File()], 42, "stable-upload-0003", "create", null,
            TestContext.Current.CancellationToken);
        await fixture.Objects.FirstPutStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        var conflict = await Assert.ThrowsAsync<CommentException>(() =>
            fixture.Service.IngestAsync(
                [File()], 42, "stable-upload-0003", "create", null,
                TestContext.Current.CancellationToken));
        fixture.Objects.ReleaseFirstPut.TrySetResult();
        await first;

        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal(1, fixture.Objects.PutCalls);
    }

    [Fact]
    public async Task Retry_recovers_completed_unrecorded_put_without_overwriting_bytes()
    {
        var fixture = Fixture();
        using var interrupted = new CancellationTokenSource();
        fixture.Objects.CancelAfterPut = interrupted;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.IngestAsync(
                [File()], 42, "stable-upload-0011", "create", null,
                interrupted.Token));

        Assert.Equal(CommentMediaIngestionState.Pending, fixture.Repository.Ingestion!.State);
        Assert.Equal(1, fixture.Objects.PutCalls);
        Assert.Single(fixture.Objects.Quarantine);

        fixture.Objects.CancelAfterPut = null;
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        var replacement = Png.ToArray();
        replacement[^1] ^= 0x01;

        var recovered = await fixture.Service.IngestAsync(
            [File(replacement)], 42, "stable-upload-0011", "create", null,
            TestContext.Current.CancellationToken);

        Assert.Equal(CommentMediaIngestionState.Promoted, recovered.Ingestion.State);
        Assert.Equal(1, fixture.Objects.PutCalls);
        Assert.Equal(Png, Assert.Single(fixture.Objects.Final).Value);
    }

    [Fact]
    public async Task Promoted_orphan_deadline_honors_configured_retention()
    {
        var fixture = Fixture();
        fixture.Options.PromotedOrphanRetention = TimeSpan.FromMinutes(10);
        var expected = fixture.Clock.GetUtcNow().UtcDateTime.AddMinutes(10);

        var result = await fixture.Service.IngestAsync(
            [File()], 42, "stable-upload-0012", "create", null,
            TestContext.Current.CancellationToken);

        Assert.Equal(CommentMediaIngestionState.Promoted, result.Ingestion.State);
        Assert.Equal(expected, result.Ingestion.NextAttemptAtUtc);
    }

    [Fact]
    public async Task Infected_batch_is_compensated_and_never_promoted()
    {
        var fixture = Fixture(ScanMode.Infected);

        var error = await Assert.ThrowsAsync<CommentException>(() =>
            fixture.Service.IngestAsync(
                [File()], 42, "stable-upload-0004", "create", null,
                TestContext.Current.CancellationToken));

        Assert.Equal(422, error.StatusCode);
        Assert.Equal(CommentMediaIngestionState.Cleaned, fixture.Repository.Ingestion!.State);
        Assert.Empty(fixture.Objects.Quarantine);
        Assert.Empty(fixture.Objects.Final);
    }

    [Fact]
    public async Task Unavailable_scanner_fails_closed_and_leaves_private_retry_state()
    {
        var fixture = Fixture(ScanMode.Unavailable);

        var error = await Assert.ThrowsAsync<CommentException>(() =>
            fixture.Service.IngestAsync(
                [File()], 42, "stable-upload-0005", "create", null,
                TestContext.Current.CancellationToken));

        Assert.Equal(503, error.StatusCode);
        Assert.Equal(CommentMediaIngestionState.RetryPending, fixture.Repository.Ingestion!.State);
        Assert.Single(fixture.Objects.Quarantine);
        Assert.Empty(fixture.Objects.Final);
    }

    [Fact]
    public async Task Unexpected_scanner_failure_also_fails_closed_and_is_bounded_for_retry()
    {
        var fixture = Fixture(ScanMode.UnexpectedUnavailable);

        var error = await Assert.ThrowsAsync<CommentException>(() =>
            fixture.Service.IngestAsync(
                [File()], 42, "stable-upload-0013", "create", null,
                TestContext.Current.CancellationToken));

        Assert.Equal(503, error.StatusCode);
        Assert.Equal(CommentMediaIngestionState.RetryPending, fixture.Repository.Ingestion!.State);
        Assert.Equal("scanner_unavailable", fixture.Repository.Ingestion.LastFailureCode);
        Assert.Single(fixture.Objects.Quarantine);
        Assert.Empty(fixture.Objects.Final);
    }

    [Fact]
    public async Task Quarantine_dependency_failure_is_retryable_before_any_write()
    {
        var fixture = Fixture();
        fixture.Objects.QuarantineExistsFailures = 1;

        var error = await Assert.ThrowsAsync<CommentException>(() =>
            fixture.Service.IngestAsync(
                [File()], 42, "stable-upload-0014", "create", null,
                TestContext.Current.CancellationToken));

        Assert.Equal(503, error.StatusCode);
        Assert.Equal(CommentMediaIngestionState.RetryPending, fixture.Repository.Ingestion!.State);
        Assert.Equal("quarantine_storage_unavailable", fixture.Repository.Ingestion.LastFailureCode);
        Assert.Equal(0, fixture.Objects.PutCalls);

        var deferred = await Assert.ThrowsAsync<CommentException>(() =>
            fixture.Service.IngestAsync(
                [File()], 42, "stable-upload-0014", "create", null,
                TestContext.Current.CancellationToken));
        Assert.Equal(503, deferred.StatusCode);
        Assert.Equal(0, fixture.Objects.PutCalls);

        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var recovered = await fixture.Service.IngestAsync(
            [File()], 42, "stable-upload-0014", "create", null,
            TestContext.Current.CancellationToken);

        Assert.Equal(CommentMediaIngestionState.Promoted, recovered.Ingestion.State);
        Assert.Equal(1, fixture.Objects.PutCalls);
    }

    [Fact]
    public async Task Promotion_failure_is_retryable_without_exposing_a_url()
    {
        var fixture = Fixture();
        fixture.Objects.FailPromotion = true;

        var error = await Assert.ThrowsAsync<CommentException>(() =>
            fixture.Service.IngestAsync(
                [File()], 42, "stable-upload-0006", "create", null,
                TestContext.Current.CancellationToken));

        Assert.Equal(503, error.StatusCode);
        Assert.Equal(CommentMediaIngestionState.RetryPending, fixture.Repository.Ingestion!.State);
        Assert.Null(fixture.Repository.Ingestion.Items[0].PrivateObjectUrl);
        Assert.Empty(fixture.Objects.Final);
    }

    [Fact]
    public async Task Cancellation_after_quarantine_is_resumed_after_lease_expiry()
    {
        var fixture = Fixture();
        using var canceled = new CancellationTokenSource();
        fixture.Objects.CancelWhenOpened = canceled;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.IngestAsync(
                [File()], 42, "stable-upload-0007", "create", null, canceled.Token));
        Assert.Equal(CommentMediaIngestionState.Quarantined, fixture.Repository.Ingestion!.State);
        Assert.Empty(fixture.Objects.Final);

        fixture.Objects.CancelWhenOpened = null;
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        var recovered = await fixture.Service.IngestAsync(
            [File()], 42, "stable-upload-0007", "create", null,
            TestContext.Current.CancellationToken);

        Assert.Equal(CommentMediaIngestionState.Promoted, recovered.Ingestion.State);
        Assert.Equal(1, fixture.Objects.PutCalls);
        Assert.Single(fixture.Objects.Final);
    }

    [Fact]
    public async Task Binding_failure_records_orphan_deadline_and_reconciler_compensates()
    {
        var fixture = Fixture();
        var result = await fixture.Service.IngestAsync(
            [File()], 42, "stable-upload-0008", "create", null,
            TestContext.Current.CancellationToken);
        fixture.Repository.FailBinding = true;

        await Assert.ThrowsAsync<IOException>(() =>
            fixture.Service.BindNewCommentAsync(
                result.Ingestion,
                null!,
                TestContext.Current.CancellationToken));
        Assert.Equal(CommentMediaIngestionState.Promoted, result.Ingestion.State);
        Assert.Equal("binding_failed", result.Ingestion.LastFailureCode);

        fixture.Clock.Advance(TimeSpan.FromHours(25));
        var cycle = fixture.Cycle();
        var reconciled = await cycle.ExecuteAsync(
            "worker-binding-recovery",
            TestContext.Current.CancellationToken);

        Assert.Equal(1, reconciled.Compensated);
        Assert.Equal(CommentMediaIngestionState.Cleaned, result.Ingestion.State);
        Assert.Empty(fixture.Objects.Final);
    }

    [Fact]
    public async Task Bound_batch_keeps_cleanup_checkpoint_when_quarantine_delete_failed()
    {
        var fixture = Fixture();
        fixture.Objects.QuarantineDeleteFailures = 1;
        var result = await fixture.Service.IngestAsync(
            [File()], 42, "stable-upload-0009", "create", null,
            TestContext.Current.CancellationToken);
        Assert.Single(fixture.Objects.Quarantine);

        result.Ingestion.MarkBound(9001, fixture.Clock.GetUtcNow().UtcDateTime);
        await fixture.Repository.SaveAsync(
            result.Ingestion,
            TestContext.Current.CancellationToken);
        Assert.NotNull(result.Ingestion.NextAttemptAtUtc);

        var reconciled = await fixture.Cycle().ExecuteAsync(
            "worker-quarantine-cleanup",
            TestContext.Current.CancellationToken);

        Assert.Equal(1, reconciled.Claimed);
        Assert.Empty(fixture.Objects.Quarantine);
        Assert.Single(fixture.Objects.Final);
        Assert.Null(result.Ingestion.NextAttemptAtUtc);
        Assert.Equal(CommentMediaIngestionState.Bound, result.Ingestion.State);
    }

    [Fact]
    public async Task Removed_bound_media_is_retried_until_the_exact_private_object_is_deleted()
    {
        var fixture = Fixture();
        var result = await fixture.Service.IngestAsync(
            [File()], 42, "stable-upload-0015", "create", null,
            TestContext.Current.CancellationToken);
        result.Ingestion.MarkBound(9002, fixture.Clock.GetUtcNow().UtcDateTime);
        result.Ingestion.ScheduleBoundObjectCleanup(
            [0],
            fixture.Clock.GetUtcNow().UtcDateTime);
        await fixture.Repository.SaveAsync(
            result.Ingestion,
            TestContext.Current.CancellationToken);
        fixture.Objects.FinalDeleteFailures = 1;

        var failed = await fixture.Cycle().ExecuteAsync(
            "worker-final-delete-failure",
            TestContext.Current.CancellationToken);

        Assert.Equal(1, failed.Retried);
        Assert.Equal(CommentMediaIngestionState.Bound, result.Ingestion.State);
        Assert.Equal(CommentMediaObjectState.CleanupPending, result.Ingestion.Items[0].State);
        Assert.Single(fixture.Objects.Final);
        Assert.Equal("bound_media_delete_failed", result.Ingestion.LastFailureCode);

        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var recovered = await fixture.Cycle().ExecuteAsync(
            "worker-final-delete-retry",
            TestContext.Current.CancellationToken);

        Assert.Equal(1, recovered.Completed);
        Assert.Equal(CommentMediaIngestionState.Cleaned, result.Ingestion.State);
        Assert.Empty(fixture.Objects.Final);
    }

    [Fact]
    public async Task Removing_one_object_keeps_the_other_bound_object_available()
    {
        var fixture = Fixture();
        var result = await fixture.Service.IngestAsync(
            [File(), File()], 42, "stable-upload-0016", "create", null,
            TestContext.Current.CancellationToken);
        result.Ingestion.MarkBound(9003, fixture.Clock.GetUtcNow().UtcDateTime);
        result.Ingestion.ScheduleBoundObjectCleanup(
            [0],
            fixture.Clock.GetUtcNow().UtcDateTime);
        await fixture.Repository.SaveAsync(
            result.Ingestion,
            TestContext.Current.CancellationToken);

        await fixture.Cycle().ExecuteAsync(
            "worker-partial-final-delete",
            TestContext.Current.CancellationToken);

        Assert.Equal(CommentMediaIngestionState.Bound, result.Ingestion.State);
        Assert.Equal(CommentMediaObjectState.Cleaned, result.Ingestion.Items[0].State);
        Assert.Equal(CommentMediaObjectState.Bound, result.Ingestion.Items[1].State);
        Assert.Single(fixture.Objects.Final);
    }

    [Fact]
    public async Task Concurrent_workers_claim_a_due_batch_only_once()
    {
        var fixture = Fixture(ScanMode.Unavailable);
        await Assert.ThrowsAsync<CommentException>(() =>
            fixture.Service.IngestAsync(
                [File()], 42, "stable-upload-0010", "create", null,
                TestContext.Current.CancellationToken));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        var first = fixture.Cycle().ExecuteAsync(
            "worker-a",
            TestContext.Current.CancellationToken);
        var second = fixture.Cycle().ExecuteAsync(
            "worker-b",
            TestContext.Current.CancellationToken);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, results.Sum(result => result.Claimed));
    }

    private static TestFixture Fixture(ScanMode mode = ScanMode.Clean)
    {
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero));
        var repository = new FakeRepository();
        var objects = new FakeObjectStore();
        var options = new CommentMediaIngestionOptions();
        var scanner = new FakeScanner(mode, clock);
        var service = new CommentMediaIngestionService(
            repository,
            objects,
            scanner,
            options,
            clock);
        return new(clock, repository, objects, scanner, options, service);
    }

    private static IncomingMediaFile File(byte[]? content = null)
    {
        content ??= Png;
        return new(
        "image.png",
        "image/png",
        content.Length,
        MediaType.Image,
        () => new MemoryStream(content));
    }

    private enum ScanMode { Clean, Infected, Unavailable, UnexpectedUnavailable }

    private sealed record TestFixture(
        MutableTimeProvider Clock,
        FakeRepository Repository,
        FakeObjectStore Objects,
        FakeScanner Scanner,
        CommentMediaIngestionOptions Options,
        CommentMediaIngestionService Service)
    {
        public CommentMediaReconciliationCycle Cycle() => new(
            Repository,
            Service,
            Options,
            Clock,
            AllowAllErasureAuthority.Instance);
    }

    private sealed class AllowAllErasureAuthority : ICommentUserErasureAuthority
    {
        public static AllowAllErasureAuthority Instance { get; } = new();

        public Task<ICommentUserMutationScope> AcquireMutationAsync(
            IReadOnlyCollection<int> userIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<ICommentUserMutationScope>(new AllowAllMutationScope());

        public Task<bool> IsErasedAsync(int userId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<IReadOnlySet<int>> GetErasedAsync(
            IReadOnlyCollection<int> userIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());
    }

    private sealed class AllowAllMutationScope : ICommentUserMutationScope
    {
        public bool Allowed => true;

        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now = _now.Add(amount);
    }

    private sealed class FakeScanner(ScanMode mode, TimeProvider clock)
        : IMediaSecurityScanner
    {
        public Task<VerifiedMediaContent> VerifyAsync(
            IncomingMediaFile file,
            Stream content,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mode == ScanMode.Infected)
                throw new CommentException("Media content was rejected by security scanning.", 422);
            if (mode == ScanMode.Unavailable)
                throw new CommentException("Media scanning is temporarily unavailable.", 503);
            if (mode == ScanMode.UnexpectedUnavailable)
                throw new InvalidOperationException("simulated scanner dependency failure");
            return Task.FromResult(new VerifiedMediaContent(
                file.FileName,
                file.MimeType,
                ".png",
                file.SizeBytes,
                Convert.ToHexStringLower(SHA256.HashData(Png)),
                "ClamAV",
                "1.4.3",
                "27422",
                clock.GetUtcNow().UtcDateTime));
        }
    }

    private sealed class FakeObjectStore : ICommentMediaObjectStore
    {
        public ConcurrentDictionary<string, byte[]> Quarantine { get; } = new();
        public ConcurrentDictionary<string, byte[]> Final { get; } = new();
        public Action? BeforePut { get; set; }
        public bool FailPromotion { get; set; }
        public int QuarantineExistsFailures { get; set; }
        public int QuarantineDeleteFailures { get; set; }
        public int FinalDeleteFailures { get; set; }
        public CancellationTokenSource? CancelWhenOpened { get; set; }
        public CancellationTokenSource? CancelAfterPut { get; set; }
        public bool PauseFirstPut { get; set; }
        public TaskCompletionSource FirstPutStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstPut { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int PutCalls { get; private set; }
        public int PromoteCalls { get; private set; }

        public async Task<CommentMediaObjectWriteResult> PutQuarantineAsync(
            int ownerUserId,
            string objectName,
            string contentType,
            Stream content,
            CancellationToken cancellationToken)
        {
            BeforePut?.Invoke();
            PutCalls++;
            if (PauseFirstPut && PutCalls == 1)
            {
                FirstPutStarted.TrySetResult();
                await ReleaseFirstPut.Task.WaitAsync(cancellationToken);
            }
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            Quarantine[Key(ownerUserId, objectName)] = copy.ToArray();
            if (CancelAfterPut is not null)
            {
                CancelAfterPut.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return new("https://private.invalid/quarantine", "quarantine-etag");
        }

        public Task<bool> QuarantineExistsAsync(
            int ownerUserId,
            string objectName,
            CancellationToken cancellationToken)
        {
            if (QuarantineExistsFailures > 0)
            {
                QuarantineExistsFailures--;
                throw new InvalidOperationException("simulated quarantine dependency failure");
            }
            return Task.FromResult(Quarantine.ContainsKey(Key(ownerUserId, objectName)));
        }

        public Task<Stream> OpenQuarantineReadAsync(
            int ownerUserId,
            string objectName,
            CancellationToken cancellationToken)
        {
            if (CancelWhenOpened is not null)
            {
                CancelWhenOpened.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return Task.FromResult<Stream>(new MemoryStream(
                Quarantine[Key(ownerUserId, objectName)],
                writable: false));
        }

        public Task<Stream> OpenFinalReadAsync(
            int ownerUserId,
            string objectName,
            CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(
                Final[Key(ownerUserId, objectName)],
                writable: false));

        public Task<CommentMediaObjectWriteResult> PromoteAsync(
            int ownerUserId,
            string quarantineObjectName,
            string finalObjectName,
            string contentType,
            long sizeBytes,
            CancellationToken cancellationToken)
        {
            PromoteCalls++;
            if (FailPromotion)
                throw new IOException("simulated promotion failure");
            Final[Key(ownerUserId, finalObjectName)] =
                Quarantine[Key(ownerUserId, quarantineObjectName)].ToArray();
            return Task.FromResult(new CommentMediaObjectWriteResult(
                finalObjectName,
                "final-etag"));
        }

        public Task DeleteQuarantineAsync(
            int ownerUserId,
            string objectName,
            CancellationToken cancellationToken)
        {
            if (QuarantineDeleteFailures > 0)
            {
                QuarantineDeleteFailures--;
                throw new IOException("simulated exact quarantine delete failure");
            }
            Quarantine.TryRemove(Key(ownerUserId, objectName), out _);
            return Task.CompletedTask;
        }

        public Task DeleteFinalAsync(
            int ownerUserId,
            string objectName,
            CancellationToken cancellationToken)
        {
            if (FinalDeleteFailures > 0)
            {
                FinalDeleteFailures--;
                throw new IOException("simulated exact final delete failure");
            }
            Final.TryRemove(Key(ownerUserId, objectName), out _);
            return Task.CompletedTask;
        }

        private static string Key(int ownerUserId, string objectName) =>
            $"{ownerUserId}/{objectName}";
    }

    private sealed class FakeRepository : ICommentMediaIngestionRepository
    {
        private readonly object _sync = new();
        public CommentMediaIngestion? Ingestion { get; private set; }
        public int SaveCount { get; private set; }
        public bool FailBinding { get; set; }

        public Task<CommentMediaIngestionGetResult> GetOrCreateAsync(
            CommentMediaIngestion candidate,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (Ingestion is null)
                {
                    Ingestion = candidate;
                    SaveCount++;
                    return Task.FromResult(new CommentMediaIngestionGetResult(candidate, true));
                }
                return Task.FromResult(new CommentMediaIngestionGetResult(Ingestion, false));
            }
        }

        public Task<CommentMediaIngestion?> FindAsync(
            string uploadIdentity,
            int ownerUserId,
            CancellationToken cancellationToken) => Task.FromResult(Ingestion);

        public Task SaveAsync(
            CommentMediaIngestion ingestion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task<bool> TryClaimForRequestAsync(
            CommentMediaIngestion ingestion,
            string leaseOwner,
            DateTime utcNow,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var claimed = ingestion.TryClaim(
                    leaseOwner,
                    utcNow,
                    leaseDuration,
                    countAttempt: false);
                if (claimed)
                    SaveCount++;
                return Task.FromResult(claimed);
            }
        }

        public Task<bool> RecordBindingFailureAsync(
            string uploadIdentity,
            int ownerUserId,
            DateTime utcNow,
            DateTime nextAttemptAtUtc,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<CommentPost> BindNewCommentAsync(
            CommentMediaIngestion ingestion,
            CommentPost comment,
            CancellationToken cancellationToken) =>
            FailBinding
                ? Task.FromException<CommentPost>(new IOException("simulated database failure"))
                : throw new NotSupportedException();

        public Task<CommentPost> BindUpdatedCommentAsync(
            CommentMediaIngestion ingestion,
            CommentPost comment,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CommentMediaIngestion>> ClaimDueAsync(
            string leaseOwner,
            DateTime utcNow,
            int batchSize,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (Ingestion is null
                    || !Ingestion.NextAttemptAtUtc.HasValue
                    || Ingestion.NextAttemptAtUtc > utcNow)
                    return Task.FromResult<IReadOnlyList<CommentMediaIngestion>>([]);
                if (Ingestion.State == CommentMediaIngestionState.Bound)
                    return Task.FromResult<IReadOnlyList<CommentMediaIngestion>>([Ingestion]);
                if (!Ingestion.TryClaim(leaseOwner, utcNow, leaseDuration))
                    return Task.FromResult<IReadOnlyList<CommentMediaIngestion>>([]);
                return Task.FromResult<IReadOnlyList<CommentMediaIngestion>>([Ingestion]);
            }
        }

        public Task<int> PurgeTerminalAsync(
            DateTime completedBeforeUtc,
            int batchSize,
            CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
