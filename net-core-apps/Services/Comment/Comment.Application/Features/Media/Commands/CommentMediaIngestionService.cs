using Comment.Application.Dtos.Media;
using Comment.Application.Options;
using Comment.Application.Providers.Storage;
using Comment.Domain.AggregatesModel.Media;
using Comment.Domain.Exceptions;
using Comment.Domain.Repositories;
using Comment.Domain.AggregatesModel;
using Inspacy.Observability;
using StarterEntities.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.Features.Media.Commands;

public sealed class CommentMediaIngestionService(
    ICommentMediaIngestionRepository repository,
    ICommentMediaObjectStore objects,
    IMediaSecurityScanner scanner,
    CommentMediaIngestionOptions options,
    TimeProvider timeProvider)
{
    private static readonly Meter Meter = PlatformInstrumentation.Meter;
    private static readonly Counter<long> Transitions = Meter.CreateCounter<long>(
        "inspacy.media.ingestion.transitions",
        unit: "{batch}");

    public async Task<CommentMediaIngestionResult> IngestAsync(
        IEnumerable<IncomingMediaFile> files,
        int ownerUserId,
        string? idempotencyKey,
        string operation,
        int? targetCommentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var incoming = ValidateBatch(files);
        var uploadIdentity = MediaUploadIdentity.Create(ownerUserId, idempotencyKey);
        var fingerprint = MediaUploadIdentity.Fingerprint(
            operation,
            targetCommentId,
            incoming);
        var now = UtcNow;
        var candidate = new CommentMediaIngestion(
            uploadIdentity,
            fingerprint,
            ownerUserId,
            operation,
            targetCommentId,
            incoming.Select((file, order) => new CommentMediaIngestionRequestItem(
                order,
                file.FileName,
                file.MimeType,
                file.SizeBytes,
                file.MediaType)),
            now);
        candidate.DeferReconciliation(now, options.RequestRecoveryDelay);

        var result = await repository.GetOrCreateAsync(candidate, cancellationToken);
        var ingestion = result.Ingestion;
        ingestion.EnsureReplayMatches(
            ownerUserId,
            fingerprint,
            operation,
            targetCommentId);

        if (ingestion.State == CommentMediaIngestionState.Bound)
            return new(ingestion, ingestion.ToCommentMedia(), IsReplay: true);
        ThrowIfTerminalFailure(ingestion);

        if (ingestion.State != CommentMediaIngestionState.Promoted)
        {
            if (!result.Created
                && ingestion.LeaseUntilUtc.HasValue
                && ingestion.LeaseUntilUtc > now)
                throw new CommentException(
                    "A request with this media idempotency key is already in progress.",
                    409);
            if (!result.Created
                && ingestion.NextAttemptAtUtc.HasValue
                && ingestion.NextAttemptAtUtc > now)
                throw new CommentException(
                    "Media ingestion is temporarily deferred; retry this idempotency key later.",
                    503);
            var claimed = await repository.TryClaimForRequestAsync(
                ingestion,
                $"api-{Guid.NewGuid():N}",
                now,
                options.RequestRecoveryDelay,
                cancellationToken);
            if (!claimed)
                throw new CommentException(
                    "A request with this media idempotency key is already in progress.",
                    409);
        }
        var byOrder = incoming.Select((file, order) => (file, order))
            .ToDictionary(value => value.order, value => value.file);
        await ResumeCoreAsync(ingestion, byOrder, cancellationToken);
        return new(ingestion, ingestion.ToCommentMedia(), IsReplay: !result.Created);
    }

    public Task ResumeAsync(
        CommentMediaIngestion ingestion,
        CancellationToken cancellationToken) =>
        ResumeCoreAsync(
            ingestion,
            new Dictionary<int, IncomingMediaFile>(),
            cancellationToken);

    public async Task<CommentPost> BindNewCommentAsync(
        CommentMediaIngestion ingestion,
        CommentPost comment,
        CancellationToken cancellationToken)
    {
        try
        {
            return await repository.BindNewCommentAsync(
                ingestion,
                comment,
                cancellationToken);
        }
        catch
        {
            await RecordBindingFailureBestEffortAsync(ingestion);
            throw;
        }
    }

    public async Task<CommentPost> BindUpdatedCommentAsync(
        CommentMediaIngestion ingestion,
        CommentPost comment,
        CancellationToken cancellationToken)
    {
        try
        {
            return await repository.BindUpdatedCommentAsync(
                ingestion,
                comment,
                cancellationToken);
        }
        catch
        {
            await RecordBindingFailureBestEffortAsync(ingestion);
            throw;
        }
    }

    public async Task CompensateAsync(
        CommentMediaIngestion ingestion,
        CancellationToken cancellationToken)
    {
        foreach (var item in ingestion.Items.OrderBy(value => value.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.State == CommentMediaObjectState.Cleaned)
                continue;

            if (item.PromotedAtUtc.HasValue)
            {
                await objects.DeleteFinalAsync(
                    ingestion.OwnerUserId,
                    RequireMediaMetadata(item.FinalObjectName, "final object name"),
                    cancellationToken);
            }
            if (item.QuarantinedAtUtc.HasValue && !item.QuarantineDeletedAtUtc.HasValue)
            {
                await objects.DeleteQuarantineAsync(
                    ingestion.OwnerUserId,
                    item.QuarantineObjectName,
                    cancellationToken);
            }

            ingestion.MarkObjectCleaned(item.Order, UtcNow);
            await repository.SaveAsync(ingestion, cancellationToken);
        }

        Transitions.Add(1, new KeyValuePair<string, object?>("outcome", "compensated"));
    }

    private async Task ResumeCoreAsync(
        CommentMediaIngestion ingestion,
        IReadOnlyDictionary<int, IncomingMediaFile> requestFiles,
        CancellationToken cancellationToken)
    {
        if (ingestion.State == CommentMediaIngestionState.Bound)
        {
            await CleanupBoundQuarantineAsync(ingestion, cancellationToken);
            return;
        }
        if (ingestion.State == CommentMediaIngestionState.CompensationPending)
        {
            await CompensateAsync(ingestion, cancellationToken);
            return;
        }
        ThrowIfTerminalFailure(ingestion);

        foreach (var item in ingestion.Items.OrderBy(value => value.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.State != CommentMediaObjectState.Pending)
                continue;

            // A provider PUT can complete immediately before the process loses
            // its database acknowledgement. Recover that immutable,
            // deterministic object first so a same-key retry never overwrites
            // bytes whose side effect already succeeded.
            bool quarantineExists;
            try
            {
                quarantineExists = await objects.QuarantineExistsAsync(
                    ingestion.OwnerUserId,
                    item.QuarantineObjectName,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                ingestion.ScheduleRetry(
                    "quarantine_storage_unavailable",
                    UtcNow,
                    options.RetryDelay(ingestion.Attempts),
                    options.MaximumAttempts);
                await repository.SaveAsync(ingestion, cancellationToken);
                Transitions.Add(1, new KeyValuePair<string, object?>("outcome", "retry"));
                throw new CommentException("Media storage is temporarily unavailable.", 503);
            }

            if (quarantineExists)
            {
                ingestion.MarkQuarantined(item.Order, "recovered", UtcNow);
                await repository.SaveAsync(ingestion, cancellationToken);
                continue;
            }

            if (requestFiles.TryGetValue(item.Order, out var requestFile))
            {
                await using var content = requestFile.OpenReadStream();
                CommentMediaObjectWriteResult stored;
                try
                {
                    stored = await objects.PutQuarantineAsync(
                        ingestion.OwnerUserId,
                        item.QuarantineObjectName,
                        item.DeclaredMimeType,
                        content,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    ingestion.ScheduleRetry(
                        "quarantine_storage_unavailable",
                        UtcNow,
                        options.RetryDelay(ingestion.Attempts),
                        options.MaximumAttempts);
                    await repository.SaveAsync(ingestion, cancellationToken);
                    Transitions.Add(1, new KeyValuePair<string, object?>("outcome", "retry"));
                    throw new CommentException("Media storage is temporarily unavailable.", 503);
                }
                ingestion.MarkQuarantined(item.Order, stored.ObjectVersion, UtcNow);
                await repository.SaveAsync(ingestion, cancellationToken);
                continue;
            }

            if (UtcNow - ingestion.CreatedDate >= options.PendingAbandonment)
            {
                ingestion.MarkOrphaned(UtcNow);
                await repository.SaveAsync(ingestion, cancellationToken);
                await CompensateAsync(ingestion, cancellationToken);
                return;
            }

            ingestion.ScheduleRetry(
                "awaiting_quarantine",
                UtcNow,
                options.RetryDelay(ingestion.Attempts),
                options.MaximumAttempts);
            await repository.SaveAsync(ingestion, cancellationToken);
            return;
        }

        foreach (var item in ingestion.Items.OrderBy(value => value.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.State != CommentMediaObjectState.Quarantined)
                continue;

            VerifiedMediaContent verified;
            try
            {
                await using var content = await objects.OpenQuarantineReadAsync(
                    ingestion.OwnerUserId,
                    item.QuarantineObjectName,
                    cancellationToken);
                var descriptor = new IncomingMediaFile(
                    item.OriginalFileName,
                    item.DeclaredMimeType,
                    item.DeclaredSizeBytes,
                    item.MediaType,
                    () => throw new InvalidOperationException(
                        "Durable media descriptors do not reopen request streams."));
                verified = await scanner.VerifyAsync(
                    descriptor,
                    content,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (CommentException exception) when (exception.StatusCode != 503)
            {
                ingestion.Reject(
                    exception.StatusCode == 422 ? "malware_rejected" : "content_rejected",
                    UtcNow);
                await repository.SaveAsync(ingestion, cancellationToken);
                await CompensateAsync(ingestion, cancellationToken);
                throw;
            }
            catch (Exception exception)
            {
                ingestion.ScheduleRetry(
                    "scanner_unavailable",
                    UtcNow,
                    options.RetryDelay(ingestion.Attempts),
                    options.MaximumAttempts);
                await repository.SaveAsync(ingestion, cancellationToken);
                Transitions.Add(1, new KeyValuePair<string, object?>("outcome", "retry"));
                throw exception is CommentException
                    ? exception
                    : new CommentException("Media scanning is temporarily unavailable.", 503);
            }

            ingestion.MarkClean(
                item.Order,
                verified.FileName,
                verified.MimeType,
                verified.Extension,
                verified.SizeBytes,
                verified.Sha256,
                verified.Scanner,
                verified.ScannerVersion,
                verified.SignatureVersion,
                verified.ScannedAtUtc,
                UtcNow);
            await repository.SaveAsync(ingestion, cancellationToken);
        }

        foreach (var item in ingestion.Items.OrderBy(value => value.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.State != CommentMediaObjectState.Clean)
                continue;

            CommentMediaObjectWriteResult promoted;
            try
            {
                promoted = await objects.PromoteAsync(
                    ingestion.OwnerUserId,
                    item.QuarantineObjectName,
                    RequireMediaMetadata(item.FinalObjectName, "final object name"),
                    RequireMediaMetadata(item.VerifiedMimeType, "verified MIME type"),
                    item.VerifiedSizeBytes.GetValueOrDefault(),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                ingestion.ScheduleRetry(
                    "promotion_failed",
                    UtcNow,
                    options.RetryDelay(ingestion.Attempts),
                    options.MaximumAttempts);
                await repository.SaveAsync(ingestion, cancellationToken);
                Transitions.Add(1, new KeyValuePair<string, object?>("outcome", "retry"));
                throw new CommentException("Media promotion is temporarily unavailable.", 503);
            }

            // Do not include this persistence boundary in the promotion catch.
            // If the copy succeeded and the database acknowledgement fails, a
            // fresh scope safely repeats the deterministic copy from the clean
            // quarantine object instead of corrupting the in-memory state into
            // RetryPending with an already-promoted item.
            ingestion.MarkPromoted(
                item.Order,
                promoted.PrivateObjectReference,
                promoted.ObjectVersion,
                options.PromotedOrphanRetention,
                UtcNow);
            await repository.SaveAsync(ingestion, cancellationToken);

            try
            {
                await objects.DeleteQuarantineAsync(
                    ingestion.OwnerUserId,
                    item.QuarantineObjectName,
                    cancellationToken);
                ingestion.MarkQuarantineDeleted(item.Order, UtcNow);
                await repository.SaveAsync(ingestion, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // The final object and durable promoted state are already safe.
                // The bounded reconciler retries exact quarantine cleanup.
                Transitions.Add(1, new KeyValuePair<string, object?>("outcome", "cleanup_retry"));
            }
        }

        if (ingestion.State == CommentMediaIngestionState.Promoted)
            Transitions.Add(1, new KeyValuePair<string, object?>("outcome", "promoted"));
    }

    public async Task CleanupDeletedFinalObjectsAsync(
        CommentMediaIngestion ingestion,
        CancellationToken cancellationToken)
    {
        if (!ingestion.HasPendingFinalObjectCleanup)
            return;

        foreach (var item in ingestion.Items
            .Where(value => value.State == CommentMediaObjectState.CleanupPending)
            .OrderBy(value => value.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await objects.DeleteFinalAsync(
                    ingestion.OwnerUserId,
                    RequireMediaMetadata(item.FinalObjectName, "final object name"),
                    cancellationToken);
                if (item.QuarantinedAtUtc.HasValue
                    && !item.QuarantineDeletedAtUtc.HasValue)
                {
                    await objects.DeleteQuarantineAsync(
                        ingestion.OwnerUserId,
                        item.QuarantineObjectName,
                        cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                ingestion.DeferBoundObjectCleanup(
                    "bound_media_delete_failed",
                    UtcNow,
                    options.RetryDelay(ingestion.Attempts));
                await repository.SaveAsync(ingestion, cancellationToken);
                throw new CommentException(
                    "Comment media deletion is temporarily unavailable.",
                    503);
            }

            ingestion.MarkBoundObjectCleaned(item.Order, UtcNow);
            await repository.SaveAsync(ingestion, cancellationToken);
        }

        Transitions.Add(1, new KeyValuePair<string, object?>(
            "outcome",
            "bound_object_deleted"));
    }

    private static List<IncomingMediaFile> ValidateBatch(IEnumerable<IncomingMediaFile> files)
    {
        var incoming = (files ?? Array.Empty<IncomingMediaFile>()).ToList();
        if (incoming.Count is < 1 or > 4)
            throw new CommentException("A media upload must contain between one and four files.");
        if (incoming.Any(file => file is null))
            throw new CommentException("A media file is required.");
        foreach (var file in incoming)
            Comment.Domain.AggregatesModel.CommentMediaRules.EnsureValid(
                file.FileName,
                file.MimeType,
                file.SizeBytes,
                file.MediaType);
        if (incoming.Sum(file => file.SizeBytes) > 30L * 1024 * 1024)
            throw new CommentException("The combined media payload exceeds the 30 MiB limit.");
        return incoming;
    }

    private async Task CleanupBoundQuarantineAsync(
        CommentMediaIngestion ingestion,
        CancellationToken cancellationToken)
    {
        foreach (var item in ingestion.Items.Where(item =>
            item.QuarantinedAtUtc.HasValue
            && !item.QuarantineDeletedAtUtc.HasValue))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await objects.DeleteQuarantineAsync(
                ingestion.OwnerUserId,
                item.QuarantineObjectName,
                cancellationToken);
            ingestion.MarkQuarantineDeleted(item.Order, UtcNow);
            await repository.SaveAsync(ingestion, cancellationToken);
        }
    }

    private static void ThrowIfTerminalFailure(CommentMediaIngestion ingestion)
    {
        if (ingestion.State == CommentMediaIngestionState.Rejected
            || ingestion.LastFailureCode == "malware_rejected")
            throw new CommentException("Media content was rejected by security scanning.", 422);
        if (ingestion.State == CommentMediaIngestionState.Cleaned)
            throw new CommentException(
                "The media ingestion is no longer resumable; use a new idempotency key.",
                409);
    }

    private async Task RecordBindingFailureBestEffortAsync(
        CommentMediaIngestion ingestion)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var now = UtcNow;
            var recorded = await repository.RecordBindingFailureAsync(
                ingestion.UploadIdentity,
                ingestion.OwnerUserId,
                now,
                now.Add(options.PromotedOrphanRetention),
                cleanup.Token);
            if (recorded)
            {
                ingestion.RestorePromotedAfterBindingRollback(
                    now,
                    options.PromotedOrphanRetention);
                Transitions.Add(1, new KeyValuePair<string, object?>("outcome", "binding_retry"));
            }
        }
        catch
        {
            // Preserve the original binding exception. The already-persisted
            // promoted state has its original bounded recovery deadline.
        }
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    private static string RequireMediaMetadata(string? value, string fieldName) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new CommentException(
                $"Persisted media {fieldName} is missing.",
                500);
}

internal static class MediaUploadIdentity
{
    public static string Create(int ownerUserId, string? idempotencyKey)
    {
        var key = idempotencyKey?.Trim();
        if (ownerUserId <= 0
            || string.IsNullOrWhiteSpace(key)
            || key.Length is < 8 or > 128
            || key.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not '.'))
            throw new CommentException(
                "Media uploads require an Idempotency-Key header containing 8-128 safe characters.");
        return Hash($"comment-media\n{ownerUserId.ToString(CultureInfo.InvariantCulture)}\n{key}");
    }

    public static string Fingerprint(
        string operation,
        int? targetCommentId,
        IReadOnlyList<IncomingMediaFile> files)
    {
        var value = new StringBuilder("comment-media-request\n")
            .Append(operation?.Trim().ToLowerInvariant()).Append('\n')
            .Append(targetCommentId?.ToString(CultureInfo.InvariantCulture) ?? "new")
            .Append('\n');
        foreach (var file in files)
        {
            value.Append(file.FileName.Normalize(NormalizationForm.FormC)).Append('\u001f')
                .Append(file.MimeType.Trim().ToLowerInvariant()).Append('\u001f')
                .Append(file.SizeBytes.ToString(CultureInfo.InvariantCulture)).Append('\u001f')
                .Append(((int)file.MediaType).ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        return Hash(value.ToString());
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
