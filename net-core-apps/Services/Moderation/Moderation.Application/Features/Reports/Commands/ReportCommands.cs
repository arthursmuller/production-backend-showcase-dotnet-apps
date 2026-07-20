using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Moderation.Application.Dtos;
using Moderation.Application.Features.Shared;
using Moderation.Application.Options;
using Moderation.Application.Providers;
using Moderation.Application.Telemetry;
using Moderation.Domain.AggregatesModel;
using Moderation.Domain.Exceptions;
using Moderation.Domain.Repositories;

namespace Moderation.Application.Features.Reports.Commands;

public sealed class ReportCommands(
    IModerationRepository repository,
    IModerationUserErasureStore erasures,
    IModerationEventPublisher publisher,
    TimeProvider timeProvider,
    IOptions<ModerationSlaOptions> slaOptions)
{
    private readonly ModerationSlaOptions _sla = slaOptions.Value;

    public async Task<ModerationResult<ModerationReportReceiptDto>> SubmitReportAsync(
        string reporterId,
        string idempotencyKey,
        SubmitModerationReportRequestDto request,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = ModerationTelemetry.ActivitySource.StartActivity("moderation.report.submit");
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var normalizedReporter = ModerationSubject.Normalize(reporterId);
            var idempotencyHash = HashIdempotencyKey(idempotencyKey);
            var mapped = MapRequest(request);
            var requestHash = HashRequest(mapped.Resource, mapped.Reason, mapped.Evidence);

            await using var transaction =
                await erasures.BeginSerializableAsync(cancellationToken);
            if (ModerationSubject.TryGetUserId(
                    normalizedReporter,
                    out var platformUserId)
                && await erasures.IsUserErasedAsync(
                    platformUserId,
                    cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return ModerationResult<ModerationReportReceiptDto>.Fail(
                    ModerationFailureKind.Forbidden,
                    "subject-unavailable",
                    "The reporting subject is unavailable.");
            }

            var existing = await repository.FindReportByIdempotencyAsync(
                normalizedReporter,
                idempotencyHash,
                cancellationToken);
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return ExistingReceipt(existing, requestHash);
            }

            var now = timeProvider.GetUtcNow();
            var reportId = Guid.NewGuid();
            var caseId = Guid.NewGuid();
            var report = ModerationReport.Create(
                reportId,
                caseId,
                normalizedReporter,
                mapped.Resource,
                mapped.Reason,
                idempotencyHash,
                requestHash,
                now,
                mapped.Evidence);
            var moderationCase = ModerationCase.Create(
                caseId,
                reportId,
                mapped.Resource,
                normalizedReporter,
                now,
                now.AddMinutes(_sla.ReceivedMinutes));

            repository.AddReport(report);
            repository.AddCase(moderationCase);
            await publisher.PublishCaseChangeAsync(moderationCase, cancellationToken);
            try
            {
                await repository.SaveChangesAsync(cancellationToken);
            }
            catch (DuplicateModerationReportException)
            {
                repository.ClearTracking();
                existing = await repository.FindReportByIdempotencyAsync(
                    normalizedReporter,
                    idempotencyHash,
                    cancellationToken);
                if (existing is null)
                    throw;
                await transaction.CommitAsync(cancellationToken);
                return ExistingReceipt(existing, requestHash);
            }

            await transaction.CommitAsync(cancellationToken);

            ModerationTelemetry.ReportsReceived.Add(1);
            activity?.SetTag("moderation.resource.kind", mapped.Resource.Kind.ToString());
            return ModerationResult<ModerationReportReceiptDto>.Success(
                new ModerationReportReceiptDto(report.Id, "Received", report.ReceivedAtUtc, false));
        }
        catch (ModerationValidationException exception)
        {
            return ModerationFeatureSupport.Validation<ModerationReportReceiptDto>(exception.Message);
        }
        finally
        {
            ModerationTelemetry.OperationDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    public Task<ModerationResult<ModerationReportStatusDto>> AppealAsync(
        Guid reportId,
        string reporterId,
        CaseTransitionRequest request,
        CancellationToken cancellationToken) =>
        ModerationFeatureSupport.ExecuteMutationAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedReporter = ModerationSubject.Normalize(reporterId);
            await using var transaction =
                await erasures.BeginSerializableAsync(cancellationToken);
            if (ModerationSubject.TryGetUserId(
                    normalizedReporter,
                    out var platformUserId)
                && await erasures.IsUserErasedAsync(
                    platformUserId,
                    cancellationToken))
            {
                throw new UnauthorizedAccessException(
                    "The report is not owned by this reporter.");
            }
            var moderationCase = await repository.GetCaseByReportAsync(reportId, cancellationToken)
                ?? throw new ModerationEntityNotFoundException("The report status was not found.");
            var now = timeProvider.GetUtcNow();
            moderationCase.Appeal(
                normalizedReporter,
                request.ReasonCode,
                request.ExpectedVersion,
                now,
                now.AddMinutes(_sla.AppealMinutes));
            await publisher.PublishCaseChangeAsync(moderationCase, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ModerationReportStatusDto(
                reportId,
                ModerationFeatureSupport.ToReporterStatus(moderationCase.Status),
                moderationCase.LastChangedAtUtc,
                moderationCase.CanAppeal,
                moderationCase.Version);
        });

    private static ModerationResult<ModerationReportReceiptDto> ExistingReceipt(
        ModerationReport existing,
        string requestHash)
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(existing.RequestHash),
                Convert.FromHexString(requestHash)))
        {
            return ModerationResult<ModerationReportReceiptDto>.Fail(
                ModerationFailureKind.IdempotencyConflict,
                "idempotency-key-reused",
                "The idempotency key was already used with a different request.");
        }

        ModerationTelemetry.DuplicateReports.Add(1);
        return ModerationResult<ModerationReportReceiptDto>.Success(
            new ModerationReportReceiptDto(existing.Id, "Received", existing.ReceivedAtUtc, true));
    }

    private static (ResourceReference Resource, ReportReason Reason, EvidenceReferenceInput[] Evidence) MapRequest(
        SubmitModerationReportRequestDto request)
    {
        if (request is null || request.Resource is null)
            throw new ModerationValidationException("A report resource is required.");
        if (request.EvidenceReferences is null)
            throw new ModerationValidationException("EvidenceReferences must be an array.");
        if (request.EvidenceReferences.Count > 16)
            throw new ModerationValidationException("A report may reference at most 16 evidence records.");

        var resource = new ResourceReference((ResourceKind)request.Resource.Kind, request.Resource.ResourceId);
        if (!Enum.IsDefined(request.ReasonCode))
            throw new ModerationValidationException("The report reason is not allow-listed.");
        var evidence = request.EvidenceReferences
            .Select(item => item ?? throw new ModerationValidationException("Evidence references cannot be null."))
            .Select(item =>
            {
                if (!Enum.IsDefined(item.Kind))
                    throw new ModerationValidationException("The evidence reference kind is not allow-listed.");
                if (string.IsNullOrWhiteSpace(item.Reference) || item.Reference.Length > 256)
                    throw new ModerationValidationException("Evidence references must be opaque and bounded.");
                if (string.IsNullOrWhiteSpace(item.Sha256)
                    || item.Sha256.Length != 64
                    || item.Sha256.Any(character => !Uri.IsHexDigit(character)))
                {
                    throw new ModerationValidationException("Evidence SHA-256 digests must be hexadecimal.");
                }
                return new EvidenceReferenceInput((EvidenceKind)item.Kind, item.Reference, item.Sha256);
            })
            .ToArray();
        return (resource, (ReportReason)request.ReasonCode, evidence);
    }

    private static string HashIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)
            || idempotencyKey.Length is < 8 or > 128
            || idempotencyKey.Any(character => character is < (char)0x21 or > (char)0x7e))
        {
            throw new ModerationValidationException(
                "Idempotency-Key must contain 8-128 printable ASCII characters.");
        }
        return Sha256(idempotencyKey);
    }

    private static string HashRequest(
        ResourceReference resource,
        ReportReason reason,
        IReadOnlyCollection<EvidenceReferenceInput> evidence)
    {
        var canonical = new StringBuilder()
            .Append((int)resource.Kind).Append(':')
            .Append(resource.ResourceId.Length).Append(':').Append(resource.ResourceId).Append(':')
            .Append((int)reason);
        foreach (var item in evidence.OrderBy(value => value.Kind)
                     .ThenBy(value => value.Reference, StringComparer.Ordinal)
                     .ThenBy(value => value.Sha256, StringComparer.OrdinalIgnoreCase))
        {
            canonical.Append('|').Append((int)item.Kind)
                .Append(':').Append(item.Reference.Length).Append(':').Append(item.Reference)
                .Append(':').Append(item.Sha256.ToLowerInvariant());
        }
        return Sha256(canonical.ToString());
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

}
