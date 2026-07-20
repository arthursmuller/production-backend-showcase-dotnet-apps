using Comment.Domain.Exceptions;
using StarterEntities;
using StarterEntities.Storage;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Comment.Domain.AggregatesModel.Media;

public enum CommentMediaIngestionState
{
    Pending = 0,
    Quarantined = 1,
    Clean = 2,
    Promoted = 3,
    Bound = 4,
    RetryPending = 5,
    CompensationPending = 6,
    Rejected = 7,
    Cleaned = 8,
}

public enum CommentMediaObjectState
{
    Pending = 0,
    Quarantined = 1,
    Clean = 2,
    Promoted = 3,
    Bound = 4,
    Rejected = 5,
    CleanupPending = 6,
    Cleaned = 7,
}

/// <summary>
/// Durable, all-or-nothing aggregate for one Comment media request. Object names
/// are opaque and deterministic so a retry can safely resume after any process
/// interruption without trusting client-supplied paths.
/// </summary>
public sealed class CommentMediaIngestion : BaseEntity, ICommentConcurrencyTracked
{
    public const int MaximumUploadIdentityLength = 64;
    public const int MaximumFailureCodeLength = 64;
    public const int MaximumLeaseOwnerLength = 128;

    public string UploadIdentity { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public int OwnerUserId { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public int? TargetCommentId { get; private set; }
    public int? BoundCommentId { get; private set; }
    public CommentMediaIngestionState State { get; private set; }
    public int Attempts { get; private set; }
    public DateTime? NextAttemptAtUtc { get; private set; }
    public DateTime? ClaimedAtUtc { get; private set; }
    public DateTime? LeaseUntilUtc { get; private set; }
    public string? LeaseOwner { get; private set; }
    public string? LastFailureCode { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public long Version { get; private set; } = 1;
    public List<CommentMediaIngestionItem> Items { get; private set; } = new();

    private CommentMediaIngestion() { }

    public CommentMediaIngestion(
        string uploadIdentity,
        string requestFingerprint,
        int ownerUserId,
        string operation,
        int? targetCommentId,
        IEnumerable<CommentMediaIngestionRequestItem>? items,
        DateTime utcNow)
    {
        UploadIdentity = RequireOpaqueHex(uploadIdentity, nameof(uploadIdentity));
        RequestFingerprint = RequireOpaqueHex(requestFingerprint, nameof(requestFingerprint));
        if (ownerUserId <= 0)
            throw new CommentException("A valid media owner is required.");
        if (string.IsNullOrWhiteSpace(operation) || operation.Length > 32)
            throw new CommentException("The media operation is invalid.");
        if (targetCommentId <= 0)
            throw new CommentException("The target comment is invalid.");

        var requested = (items ?? Array.Empty<CommentMediaIngestionRequestItem>()).ToList();
        if (requested.Count is < 1 or > 4)
            throw new CommentException("A media ingestion must contain between one and four files.");
        if (requested.Select(item => item.Order).Distinct().Count() != requested.Count
            || requested.Any(item => item.Order < 0))
            throw new CommentException("Media display order must be unique and nonnegative.");

        OwnerUserId = ownerUserId;
        Operation = operation.Trim().ToLowerInvariant();
        TargetCommentId = targetCommentId;
        State = CommentMediaIngestionState.Pending;
        _createdDate = EnsureUtc(utcNow);
        UpdateDate = _createdDate;
        NextAttemptAtUtc = _createdDate.AddMinutes(5);
        Items = requested
            .OrderBy(item => item.Order)
            .Select(item => new CommentMediaIngestionItem(
                UploadIdentity,
                item,
                _createdDate))
            .ToList();
    }

    public void EnsureReplayMatches(
        int ownerUserId,
        string requestFingerprint,
        string operation,
        int? targetCommentId)
    {
        if (ownerUserId != OwnerUserId
            || !string.Equals(RequestFingerprint, requestFingerprint, StringComparison.Ordinal)
            || !string.Equals(Operation, operation, StringComparison.OrdinalIgnoreCase)
            || TargetCommentId != targetCommentId)
        {
            throw new CommentException(
                "The media idempotency key was already used for a different request.",
                409);
        }
    }

    public bool TryClaim(
        string leaseOwner,
        DateTime utcNow,
        TimeSpan leaseDuration,
        bool countAttempt = true)
    {
        utcNow = EnsureUtc(utcNow);
        if (IsTerminal || (LeaseUntilUtc.HasValue && LeaseUntilUtc > utcNow))
            return false;
        if (string.IsNullOrWhiteSpace(leaseOwner)
            || leaseOwner.Length > MaximumLeaseOwnerLength
            || leaseOwner.Any(char.IsControl))
            throw new CommentException("The media reconciliation lease owner is invalid.");
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(30))
            throw new CommentException("The media reconciliation lease duration is invalid.");

        LeaseOwner = leaseOwner.Trim();
        ClaimedAtUtc = utcNow;
        LeaseUntilUtc = utcNow.Add(leaseDuration);
        if (countAttempt)
            Attempts = checked(Attempts + 1);
        Touch(utcNow);
        return true;
    }

    public void DeferReconciliation(DateTime utcNow, TimeSpan delay)
    {
        utcNow = EnsureUtc(utcNow);
        if (delay <= TimeSpan.Zero || delay > TimeSpan.FromMinutes(30))
            throw new CommentException("The media reconciliation deferral is invalid.");
        NextAttemptAtUtc = utcNow.Add(delay);
        Touch(utcNow);
    }

    public void MarkQuarantined(int order, string objectVersion, DateTime utcNow)
    {
        var item = Item(order);
        item.MarkQuarantined(objectVersion, utcNow);
        if (Items.All(value => value.State >= CommentMediaObjectState.Quarantined))
            State = CommentMediaIngestionState.Quarantined;
        ClearRetry(utcNow);
    }

    public void MarkClean(
        int order,
        string canonicalFileName,
        string mimeType,
        string extension,
        long sizeBytes,
        string sha256,
        string scanner,
        string scannerVersion,
        string signatureVersion,
        DateTime scannedAtUtc,
        DateTime utcNow)
    {
        var item = Item(order);
        item.MarkClean(
            canonicalFileName,
            mimeType,
            extension,
            sizeBytes,
            sha256,
            scanner,
            scannerVersion,
            signatureVersion,
            scannedAtUtc,
            utcNow);
        if (Items.All(value => value.State >= CommentMediaObjectState.Clean))
            State = CommentMediaIngestionState.Clean;
        ClearRetry(utcNow);
    }

    public void MarkPromoted(
        int order,
        string privateObjectReference,
        string objectVersion,
        TimeSpan orphanRetention,
        DateTime utcNow)
    {
        if (orphanRetention < TimeSpan.FromMinutes(5)
            || orphanRetention > TimeSpan.FromDays(7))
            throw new CommentException("The media orphan-retention window is invalid.");
        Item(order).MarkPromoted(privateObjectReference, objectVersion, utcNow);
        if (Items.All(value => value.State >= CommentMediaObjectState.Promoted))
        {
            State = CommentMediaIngestionState.Promoted;
            NextAttemptAtUtc = EnsureUtc(utcNow).Add(orphanRetention);
            ClearLease(utcNow);
        }
        else
        {
            Touch(utcNow);
        }
    }

    public void MarkQuarantineDeleted(int order, DateTime utcNow)
    {
        Item(order).MarkQuarantineDeleted(utcNow);
        if (State == CommentMediaIngestionState.Bound
            && Items.All(item => !item.QuarantinedAtUtc.HasValue
                || item.QuarantineDeletedAtUtc.HasValue))
            NextAttemptAtUtc = null;
        Touch(utcNow);
    }

    public void MarkBound(int commentId, DateTime utcNow)
    {
        if (commentId <= 0)
            throw new CommentException("The bound comment id is invalid.");
        if (State != CommentMediaIngestionState.Promoted
            || Items.Any(item => item.State != CommentMediaObjectState.Promoted))
            throw new CommentException("Only a completely promoted media batch can be bound.");

        foreach (var item in Items)
            item.MarkBound(utcNow);
        BoundCommentId = commentId;
        State = CommentMediaIngestionState.Bound;
        CompletedAtUtc = EnsureUtc(utcNow);
        ClearLease(utcNow);
        NextAttemptAtUtc = Items.Any(item =>
            item.QuarantinedAtUtc.HasValue
            && !item.QuarantineDeletedAtUtc.HasValue)
            ? EnsureUtc(utcNow)
            : null;
    }

    public void Reject(string failureCode, DateTime utcNow)
    {
        LastFailureCode = NormalizeFailureCode(failureCode);
        foreach (var item in Items.Where(value => value.State != CommentMediaObjectState.Cleaned))
            item.MarkRejected(utcNow);
        State = Items.Any(value => value.HasStoredObject)
            ? CommentMediaIngestionState.CompensationPending
            : CommentMediaIngestionState.Rejected;
        NextAttemptAtUtc = EnsureUtc(utcNow);
        ClearLease(utcNow);
    }

    public void ScheduleRetry(
        string failureCode,
        DateTime utcNow,
        TimeSpan delay,
        int maximumAttempts)
    {
        if (maximumAttempts is < 1 or > 100)
            throw new CommentException("The media retry limit is invalid.");
        LastFailureCode = NormalizeFailureCode(failureCode);
        utcNow = EnsureUtc(utcNow);
        if (Attempts >= maximumAttempts)
        {
            foreach (var item in Items.Where(value => value.State != CommentMediaObjectState.Cleaned))
                item.MarkCleanupPending(utcNow);
            State = CommentMediaIngestionState.CompensationPending;
            NextAttemptAtUtc = utcNow;
        }
        else
        {
            State = CommentMediaIngestionState.RetryPending;
            NextAttemptAtUtc = utcNow.Add(delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
        }
        ClearLease(utcNow);
    }

    public void MarkOrphaned(DateTime utcNow)
    {
        if (State is CommentMediaIngestionState.Bound or CommentMediaIngestionState.Cleaned)
            return;
        foreach (var item in Items.Where(value => value.State != CommentMediaObjectState.Cleaned))
            item.MarkCleanupPending(utcNow);
        LastFailureCode = "binding_orphan";
        State = CommentMediaIngestionState.CompensationPending;
        NextAttemptAtUtc = EnsureUtc(utcNow);
        ClearLease(utcNow);
    }

    public void RestorePromotedAfterBindingRollback(
        DateTime utcNow,
        TimeSpan recoveryWindow)
    {
        if (State is not (CommentMediaIngestionState.Promoted
            or CommentMediaIngestionState.Bound))
            throw new CommentException("Only promoted media can await binding recovery.");
        if (recoveryWindow < TimeSpan.FromMinutes(5) || recoveryWindow > TimeSpan.FromDays(7))
            throw new CommentException("The media binding recovery window is invalid.");
        utcNow = EnsureUtc(utcNow);
        foreach (var item in Items)
            item.RestorePromotedAfterBindingRollback();
        State = CommentMediaIngestionState.Promoted;
        BoundCommentId = null;
        CompletedAtUtc = null;
        LastFailureCode = "binding_failed";
        NextAttemptAtUtc = utcNow.Add(recoveryWindow);
        ClearLease(utcNow);
    }

    public void MarkObjectCleaned(int order, DateTime utcNow)
    {
        Item(order).MarkCleaned(utcNow);
        if (Items.All(value => value.State == CommentMediaObjectState.Cleaned))
        {
            State = CommentMediaIngestionState.Cleaned;
            CompletedAtUtc = EnsureUtc(utcNow);
            NextAttemptAtUtc = null;
        }
        ClearLease(utcNow);
    }

    /// <summary>
    /// Converts selected bound objects into durable deletion work. The owning
    /// comment mutation and this checkpoint are committed in the same database
    /// transaction; the media reconciler performs the idempotent object delete.
    /// </summary>
    public void ScheduleBoundObjectCleanup(
        IEnumerable<int>? orders,
        DateTime utcNow)
    {
        if (State != CommentMediaIngestionState.Bound || BoundCommentId is null)
            throw new CommentException("Only bound media can be scheduled for deletion.");

        var selected = (orders ?? Array.Empty<int>()).Distinct().ToArray();
        if (selected.Length == 0)
            throw new CommentException("At least one bound media object is required for deletion.");

        foreach (var order in selected)
        {
            var item = Item(order);
            if (item.State != CommentMediaObjectState.Bound)
                throw new CommentException("The bound media object is not available for deletion.");
            item.MarkCleanupPending(utcNow);
        }

        LastFailureCode = "bound_media_deleted";
        NextAttemptAtUtc = EnsureUtc(utcNow);
        ClearLease(utcNow);
    }

    public void DeferBoundObjectCleanup(
        string failureCode,
        DateTime utcNow,
        TimeSpan retryDelay)
    {
        if (!HasPendingFinalObjectCleanup)
            throw new CommentException("No bound media deletion is pending.");

        utcNow = EnsureUtc(utcNow);
        LastFailureCode = NormalizeFailureCode(failureCode);
        NextAttemptAtUtc = utcNow.Add(retryDelay < TimeSpan.Zero
            ? TimeSpan.Zero
            : retryDelay);
        ClearLease(utcNow);
    }

    public void MarkBoundObjectCleaned(int order, DateTime utcNow)
    {
        var item = Item(order);
        if (item.State != CommentMediaObjectState.CleanupPending)
            throw new CommentException("The bound media object is not pending deletion.");

        item.MarkCleaned(utcNow);
        if (Items.All(value => value.State == CommentMediaObjectState.Cleaned))
        {
            State = CommentMediaIngestionState.Cleaned;
            BoundCommentId = null;
            CompletedAtUtc = EnsureUtc(utcNow);
            NextAttemptAtUtc = null;
        }
        else
        {
            State = CommentMediaIngestionState.Bound;
            NextAttemptAtUtc = Items.Any(value =>
                value.State == CommentMediaObjectState.CleanupPending
                || (value.QuarantinedAtUtc.HasValue
                    && !value.QuarantineDeletedAtUtc.HasValue))
                ? EnsureUtc(utcNow)
                : null;
        }
        ClearLease(utcNow);
    }

    public IReadOnlyList<NewCommentMedia> ToCommentMedia()
    {
        if (State is not (CommentMediaIngestionState.Promoted or CommentMediaIngestionState.Bound)
            || Items.Any(item => string.IsNullOrWhiteSpace(item.PrivateObjectUrl)))
            throw new CommentException("Media is not ready to be attached to a comment.");

        return Items.OrderBy(item => item.Order).Select(item =>
        {
            var privateObjectReference = item.PrivateObjectUrl;
            var canonicalFileName = item.CanonicalFileName;
            var verifiedMimeType = item.VerifiedMimeType;
            if (string.IsNullOrWhiteSpace(privateObjectReference)
                || string.IsNullOrWhiteSpace(canonicalFileName)
                || string.IsNullOrWhiteSpace(verifiedMimeType)
                || !item.VerifiedSizeBytes.HasValue)
                throw new CommentException("Media metadata is incomplete.");

            return new NewCommentMedia(
                item.Order,
                privateObjectReference,
                canonicalFileName,
                item.MediaType,
                null,
                null,
                verifiedMimeType,
                item.VerifiedSizeBytes.Value);
        }).ToList();
    }

    public bool IsTerminal => State is CommentMediaIngestionState.Bound
        or CommentMediaIngestionState.Rejected
        or CommentMediaIngestionState.Cleaned;

    public bool HasPendingFinalObjectCleanup =>
        State == CommentMediaIngestionState.Bound
        && Items.Any(item =>
            item.State == CommentMediaObjectState.CleanupPending
            && item.PromotedAtUtc.HasValue);

    private CommentMediaIngestionItem Item(int order) =>
        Items.SingleOrDefault(value => value.Order == order)
        ?? throw new CommentException("The media ingestion item does not exist.");

    private void ClearRetry(DateTime utcNow)
    {
        LastFailureCode = null;
        Touch(utcNow);
    }

    private void ClearLease(DateTime utcNow)
    {
        LeaseOwner = null;
        LeaseUntilUtc = null;
        Touch(utcNow);
    }

    private void Touch(DateTime utcNow) => UpdateDate = EnsureUtc(utcNow);

    private static string NormalizeFailureCode(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > MaximumFailureCodeLength
            || normalized.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
            return "unknown";
        return normalized;
    }

    private static string RequireOpaqueHex(string? value, string parameter)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized?.Length != MaximumUploadIdentityLength
            || normalized.Any(character => !char.IsAsciiHexDigit(character)))
            throw new CommentException($"The media {parameter} is invalid.");
        return normalized;
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}

public sealed record CommentMediaIngestionRequestItem(
    int Order,
    string FileName,
    string MimeType,
    long SizeBytes,
    MediaType MediaType);

public sealed class CommentMediaIngestionItem
{
    public int Order { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string DeclaredMimeType { get; private set; } = string.Empty;
    public long DeclaredSizeBytes { get; private set; }
    public MediaType MediaType { get; private set; }
    public string QuarantineObjectName { get; private set; } = string.Empty;
    public string? QuarantineObjectVersion { get; private set; }
    public DateTime? QuarantinedAtUtc { get; private set; }
    public DateTime? QuarantineDeletedAtUtc { get; private set; }
    public string? CanonicalFileName { get; private set; }
    public string? VerifiedMimeType { get; private set; }
    public string? CanonicalExtension { get; private set; }
    public long? VerifiedSizeBytes { get; private set; }
    public string? Sha256 { get; private set; }
    public string? Scanner { get; private set; }
    public string? ScannerVersion { get; private set; }
    public string? SignatureVersion { get; private set; }
    public DateTime? ScannedAtUtc { get; private set; }
    public string? FinalObjectName { get; private set; }
    public string? FinalObjectVersion { get; private set; }
    public string? PrivateObjectUrl { get; private set; }
    public DateTime? PromotedAtUtc { get; private set; }
    public DateTime? BoundAtUtc { get; private set; }
    public DateTime? CleanedAtUtc { get; private set; }
    public CommentMediaObjectState State { get; private set; }

    private CommentMediaIngestionItem() { }

    internal CommentMediaIngestionItem(
        string uploadIdentity,
        CommentMediaIngestionRequestItem request,
        DateTime utcNow)
    {
        CommentMediaRules.EnsureValid(
            request.FileName,
            request.MimeType,
            request.SizeBytes,
            request.MediaType);
        Order = request.Order;
        OriginalFileName = request.FileName.Trim();
        DeclaredMimeType = request.MimeType.Trim();
        DeclaredSizeBytes = request.SizeBytes;
        MediaType = request.MediaType;
        QuarantineObjectName = $"{uploadIdentity}-{request.Order:D2}.blob";
        State = CommentMediaObjectState.Pending;
    }

    public bool HasStoredObject => CleanedAtUtc is null
        && (QuarantinedAtUtc.HasValue || PromotedAtUtc.HasValue);

    internal void MarkQuarantined(string version, DateTime utcNow)
    {
        if (State >= CommentMediaObjectState.Quarantined)
            return;
        QuarantineObjectVersion = NormalizeVersion(version);
        QuarantinedAtUtc = utcNow;
        State = CommentMediaObjectState.Quarantined;
    }

    internal void MarkClean(
        string fileName,
        string mimeType,
        string extension,
        long sizeBytes,
        string sha256,
        string scanner,
        string scannerVersion,
        string signatureVersion,
        DateTime scannedAtUtc,
        DateTime utcNow)
    {
        if (State >= CommentMediaObjectState.Clean)
            return;
        if (State != CommentMediaObjectState.Quarantined)
            throw new CommentException("Media must be quarantined before scanning.");
        CommentMediaRules.EnsureValid(fileName, mimeType, sizeBytes, MediaType);
        if (sha256 is null || sha256.Length != 64 || sha256.Any(value => !char.IsAsciiHexDigit(value)))
            throw new CommentException("The media content hash is invalid.");
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 11 || extension[0] != '.')
            throw new CommentException("The canonical media extension is invalid.");

        CanonicalFileName = fileName.Trim();
        VerifiedMimeType = mimeType.Trim();
        CanonicalExtension = extension.Trim().ToLowerInvariant();
        VerifiedSizeBytes = sizeBytes;
        Sha256 = sha256.ToLowerInvariant();
        Scanner = NormalizeEvidence(scanner, 64);
        ScannerVersion = NormalizeEvidence(scannerVersion, 128);
        SignatureVersion = NormalizeEvidence(signatureVersion, 128);
        ScannedAtUtc = scannedAtUtc;
        FinalObjectName = PathSafeFinalName(QuarantineObjectName, CanonicalExtension);
        State = CommentMediaObjectState.Clean;
    }

    internal void MarkPromoted(
        string privateObjectReference,
        string version,
        DateTime utcNow)
    {
        if (State >= CommentMediaObjectState.Promoted)
            return;
        if (State != CommentMediaObjectState.Clean)
            throw new CommentException("Only clean media can be promoted.");
        var normalizedReference = privateObjectReference?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReference)
            || !string.Equals(normalizedReference, FinalObjectName, StringComparison.Ordinal))
            throw new CommentException("The private media object reference is invalid.");
        // PrivateObjectUrl is the existing persistence column name. New values are
        // opaque object identities, never provider/CDN URLs.
        PrivateObjectUrl = normalizedReference;
        FinalObjectVersion = NormalizeVersion(version);
        PromotedAtUtc = utcNow;
        State = CommentMediaObjectState.Promoted;
    }

    internal void MarkQuarantineDeleted(DateTime utcNow) =>
        QuarantineDeletedAtUtc ??= utcNow;

    internal void MarkBound(DateTime utcNow)
    {
        if (State != CommentMediaObjectState.Promoted)
            throw new CommentException("Only promoted media can be bound.");
        BoundAtUtc = utcNow;
        State = CommentMediaObjectState.Bound;
    }

    internal void RestorePromotedAfterBindingRollback()
    {
        if (State == CommentMediaObjectState.Bound)
        {
            BoundAtUtc = null;
            State = CommentMediaObjectState.Promoted;
        }
    }

    internal void MarkRejected(DateTime utcNow)
    {
        if (State == CommentMediaObjectState.Cleaned)
            return;
        State = HasStoredObject
            ? CommentMediaObjectState.CleanupPending
            : CommentMediaObjectState.Rejected;
    }

    internal void MarkCleanupPending(DateTime utcNow)
    {
        if (State != CommentMediaObjectState.Cleaned)
            State = CommentMediaObjectState.CleanupPending;
    }

    internal void MarkCleaned(DateTime utcNow)
    {
        PrivateObjectUrl = null;
        State = CommentMediaObjectState.Cleaned;
        CleanedAtUtc = utcNow;
    }

    private static string NormalizeVersion(string value) =>
        string.IsNullOrWhiteSpace(value) ? "not-reported" : value.Trim()[..Math.Min(value.Trim().Length, 256)];

    private static string NormalizeEvidence(string value, int maximum)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > maximum
            || normalized.Any(char.IsControl))
            throw new CommentException("Media scanner provenance is invalid.");
        return normalized;
    }

    private static string PathSafeFinalName(string quarantineName, string extension)
    {
        var stem = quarantineName.EndsWith(".blob", StringComparison.Ordinal)
            ? quarantineName[..^5]
            : quarantineName;
        return stem + extension;
    }
}
