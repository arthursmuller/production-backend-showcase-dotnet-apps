using System.Security.Claims;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Moderation.Application.Dtos;
using Moderation.Application.Features.Cases.Commands;
using Moderation.Application.Features.Cases.Queries;
using Moderation.Application.Features.Exports.Queries;
using Moderation.Application.Features.Reports.Commands;
using Moderation.Application.Features.Reports.Queries;

namespace Moderation.API.AppInterfaces.Routes;

public static class ModerationRoutes
{
    public static IEndpointRouteBuilder MapModerationRoutes(this IEndpointRouteBuilder routes)
    {
        var reporter = routes.MapGroup("/api/moderation")
            .WithTags("Moderation reports")
            .RequireAuthorization(ModerationPolicies.Reporter);
        reporter.MapPost("/reports", SubmitReport)
            .WithName("SubmitModerationReport")
            .Accepts<SubmitModerationReportRequestDto>("application/json")
            .Produces<ModerationReportReceiptDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
        reporter.MapGet("/reports/{reportId:guid}/receipt", GetReceipt)
            .WithName("GetModerationReportReceipt")
            .Produces<ModerationReportReceiptDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        reporter.MapGet("/reports/{reportId:guid}/status", GetStatus)
            .WithName("GetModerationReportStatus")
            .Produces<ModerationReportStatusDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        reporter.MapPost("/reports/{reportId:guid}/appeal", Appeal)
            .WithName("AppealModerationReport")
            .Accepts<CaseTransitionRequest>("application/json")
            .Produces<ModerationReportStatusDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        var triage = routes.MapGroup("/internal/moderation/triage")
            .WithTags("Moderation triage")
            .RequireAuthorization(ModerationPolicies.Triage);
        triage.MapGet("/cases", ListCases)
            .WithName("ListModerationTriageCases")
            .Produces<CursorPage<ModerationCaseSummary>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        triage.MapGet("/cases/{caseId:guid}", GetInternalCase)
            .WithName("GetModerationTriageCase")
            .Produces<ModerationCaseDetails>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        triage.MapPost("/cases/{caseId:guid}", Triage)
            .WithName("TriageModerationCase")
            .Accepts<TriageCaseRequest>("application/json")
            .Produces<ModerationCaseDetails>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
        triage.MapPost("/cases/{caseId:guid}/assignment", Assign)
            .WithName("AssignModerationCase")
            .Accepts<AssignCaseRequest>("application/json")
            .Produces<ModerationCaseDetails>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        var investigation = routes.MapGroup("/internal/moderation/investigation")
            .WithTags("Moderation investigations")
            .RequireAuthorization(ModerationPolicies.Investigator);
        investigation.MapGet("/cases/{caseId:guid}", GetInternalCase)
            .WithName("GetModerationInvestigationCase")
            .Produces<ModerationCaseDetails>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        investigation.MapPost("/cases/{caseId:guid}/start", StartInvestigation)
            .WithName("StartModerationInvestigation")
            .Accepts<CaseTransitionRequest>("application/json")
            .Produces<ModerationCaseDetails>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
        investigation.MapPost("/cases/{caseId:guid}/complete", CompleteInvestigation)
            .WithName("CompleteModerationInvestigation")
            .Accepts<CaseTransitionRequest>("application/json")
            .Produces<ModerationCaseDetails>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
        investigation.MapGet("/cases/{caseId:guid}/messages", GetMessages)
            .WithName("ListModerationCaseMessages")
            .Produces<IReadOnlyList<CaseMessageDetails>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        investigation.MapPost("/cases/{caseId:guid}/messages", AddMessage)
            .WithName("AddModerationCaseMessage")
            .Accepts<AddCaseMessageRequest>("application/json")
            .Produces<CaseMessageDetails>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        var decision = routes.MapGroup("/internal/moderation/decision")
            .WithTags("Moderation decisions")
            .RequireAuthorization(ModerationPolicies.Decision);
        decision.MapGet("/cases/{caseId:guid}", GetInternalCase)
            .WithName("GetModerationDecisionCase")
            .Produces<ModerationCaseDetails>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        decision.MapPost("/cases/{caseId:guid}", Decide)
            .WithName("DecideModerationCase")
            .Accepts<DecideCaseRequest>("application/json")
            .Produces<ModerationCaseDetails>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        var admin = routes.MapGroup("/internal/moderation/admin")
            .WithTags("Moderation administration")
            .RequireAuthorization(ModerationPolicies.Admin);
        admin.MapGet("/cases", ListCases)
            .WithName("ListModerationAdminCases")
            .Produces<CursorPage<ModerationCaseSummary>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        admin.MapGet("/cases/{caseId:guid}", GetInternalCase)
            .WithName("GetModerationAdminCase")
            .Produces<ModerationCaseDetails>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPost("/cases/{caseId:guid}/close", Close)
            .WithName("CloseModerationCase")
            .Accepts<CaseTransitionRequest>("application/json")
            .Produces<ModerationCaseDetails>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
        admin.MapGet("/cases/{caseId:guid}/audit", GetAudit)
            .WithName("GetModerationCaseAudit")
            .Produces<IReadOnlyList<AuditEntryDetails>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPost("/cases/{caseId:guid}/legal-holds", PlaceLegalHold)
            .WithName("PlaceModerationLegalHold")
            .Accepts<LegalHoldRequest>("application/json")
            .Produces<LegalHoldDetails>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
        admin.MapPost("/cases/{caseId:guid}/legal-holds/release", ReleaseLegalHold)
            .WithName("ReleaseModerationLegalHold")
            .Accepts<LegalHoldRequest>("application/json")
            .Produces<LegalHoldDetails>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        routes.MapGet("/internal/moderation/decision-state", ExportDecisionState)
            .WithTags("Moderation decision authority")
            .WithName("ExportModerationDecisionState")
            .WithSummary("Exports a bounded service-only decision-state authority page without report or evidence data.")
            .RequireAuthorization(ModerationPolicies.DecisionExport)
            .Produces<ModerationDecisionExportPageDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return routes;
    }

    internal static async Task<Results<Created<ModerationReportReceiptDto>, ProblemHttpResult>> SubmitReport(
        HttpContext context,
        SubmitModerationReportRequestDto request,
        [FromServices] ReportCommands service,
        CancellationToken cancellationToken)
    {
        var actor = ActorId(context);
        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
        var result = await service.SubmitReportAsync(actor, idempotencyKey, request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/moderation/reports/{result.Value!.ReportId:D}/receipt", result.Value)
            : Problem(result.Failure!);
    }

    internal static async Task<Results<Ok<ModerationReportReceiptDto>, ProblemHttpResult>> GetReceipt(
        Guid reportId,
        HttpContext context,
        [FromServices] ReportQueries service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetReceiptAsync(reportId, ActorId(context), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : Problem(result.Failure!);
    }

    internal static async Task<Results<Ok<ModerationReportStatusDto>, ProblemHttpResult>> GetStatus(
        Guid reportId,
        HttpContext context,
        [FromServices] ReportQueries service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetReporterStatusAsync(reportId, ActorId(context), cancellationToken);
        if (result.IsSuccess)
            SetEtag(context, result.Value!.Version);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : Problem(result.Failure!);
    }

    internal static async Task<Results<Ok<ModerationReportStatusDto>, ProblemHttpResult>> Appeal(
        Guid reportId,
        CaseTransitionRequest request,
        HttpContext context,
        [FromServices] ReportCommands service,
        CancellationToken cancellationToken)
    {
        if (!TryExpectedVersion(context, out var expectedVersion, out var precondition))
            return precondition;
        request = request with { ExpectedVersion = expectedVersion };
        var result = await service.AppealAsync(
            reportId,
            ActorId(context),
            request,
            cancellationToken);
        if (result.IsSuccess)
            SetEtag(context, result.Value!.Version);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : Problem(result.Failure!);
    }

    internal static async Task<Results<Ok<CursorPage<ModerationCaseSummary>>, ProblemHttpResult>> ListCases(
        string? cursor,
        int? pageSize,
        [FromServices] CaseQueries service,
        CancellationToken cancellationToken)
    {
        var result = await service.ListInternalCasesAsync(cursor, pageSize ?? 50, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : Problem(result.Failure!);
    }

    internal static async Task<Results<Ok<ModerationCaseDetails>, ProblemHttpResult>> GetInternalCase(
        Guid caseId,
        HttpContext context,
        [FromServices] CaseQueries service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetInternalCaseAsync(caseId, cancellationToken);
        if (result.IsSuccess)
            SetEtag(context, result.Value!.Version);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : Problem(result.Failure!);
    }

    internal static async Task<Results<Ok<ModerationCaseDetails>, ProblemHttpResult>> Triage(
        Guid caseId,
        TriageCaseRequest request,
        HttpContext context,
        [FromServices] CaseCommands service,
        CancellationToken cancellationToken)
    {
        if (!TryExpectedVersion(context, out var expectedVersion, out var precondition))
            return precondition;
        request = request with { ExpectedVersion = expectedVersion };
        var result = await service.TriageAsync(caseId, ActorId(context), request, cancellationToken);
        if (result.IsSuccess)
            SetEtag(context, result.Value!.Version);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : Problem(result.Failure!);
    }

    internal static async Task<Results<Ok<ModerationCaseDetails>, ProblemHttpResult>> Assign(
        Guid caseId,
        AssignCaseRequest request,
        HttpContext context,
        [FromServices] CaseCommands service,
        CancellationToken cancellationToken)
    {
        if (!TryExpectedVersion(context, out var expectedVersion, out var precondition))
            return precondition;
        request = request with { ExpectedVersion = expectedVersion };
        var result = await service.AssignAsync(caseId, ActorId(context), request, cancellationToken);
        if (result.IsSuccess)
            SetEtag(context, result.Value!.Version);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : Problem(result.Failure!);
    }

    internal static async Task<Results<Ok<ModerationCaseDetails>, ProblemHttpResult>> StartInvestigation(
        Guid caseId,
        CaseTransitionRequest request,
        HttpContext context,
        [FromServices] CaseCommands service,
        CancellationToken cancellationToken)
    {
        if (!TryExpectedVersion(context, out var expectedVersion, out var precondition))
            return precondition;
        request = request with { ExpectedVersion = expectedVersion };
        var result = await service.StartInvestigationAsync(
            caseId,
            ActorId(context),
            request,
            cancellationToken);
        if (result.IsSuccess)
            SetEtag(context, result.Value!.Version);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : Problem(result.Failure!);
    }

    internal static async Task<Results<Ok<ModerationCaseDetails>, ProblemHttpResult>> CompleteInvestigation(
        Guid caseId,
        CaseTransitionRequest request,
        HttpContext context,
        [FromServices] CaseCommands service,
        CancellationToken cancellationToken)
    {
        if (!TryExpectedVersion(context, out var expectedVersion, out var precondition))
            return precondition;
        request = request with { ExpectedVersion = expectedVersion };
        var result = await service.CompleteInvestigationAsync(
            caseId,
            ActorId(context),
            request,
            cancellationToken);
        if (result.IsSuccess)
            SetEtag(context, result.Value!.Version);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : Problem(result.Failure!);
    }

    internal static async Task<Results<Ok<ModerationCaseDetails>, ProblemHttpResult>> Decide(
        Guid caseId,
        DecideCaseRequest request,
        HttpContext context,
        [FromServices] CaseCommands service,
        CancellationToken cancellationToken)
    {
        if (!TryExpectedVersion(context, out var expectedVersion, out var precondition))
            return precondition;
        request = request with { ExpectedVersion = expectedVersion };
        var result = await service.DecideAsync(caseId, ActorId(context), request, cancellationToken);
        if (result.IsSuccess)
            SetEtag(context, result.Value!.Version);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : Problem(result.Failure!);
    }

    internal static async Task<Results<Ok<IReadOnlyList<CaseMessageDetails>>, ProblemHttpResult>> GetMessages(
        Guid caseId,
        int? pageSize,
        [FromServices] CaseQueries service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetCaseMessagesAsync(caseId, pageSize ?? 100, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : Problem(result.Failure!);
    }

    internal static async Task<Results<Ok<CaseMessageDetails>, ProblemHttpResult>> AddMessage(
        Guid caseId,
        AddCaseMessageRequest request,
        HttpContext context,
        [FromServices] CaseMessageCommands service,
        CancellationToken cancellationToken)
    {
        if (!TryExpectedVersion(context, out var expectedVersion, out var precondition))
            return precondition;
        request = request with { ExpectedVersion = expectedVersion };
        var result = await service.AddCaseMessageAsync(
            caseId,
            ActorId(context),
            request,
            cancellationToken);
        if (result.IsSuccess)
            SetEtag(context, result.Value!.CaseVersion);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : Problem(result.Failure!);
    }

    internal static async Task<Results<Ok<ModerationCaseDetails>, ProblemHttpResult>> Close(
        Guid caseId,
        CaseTransitionRequest request,
        HttpContext context,
        [FromServices] CaseCommands service,
        CancellationToken cancellationToken)
    {
        if (!TryExpectedVersion(context, out var expectedVersion, out var precondition))
            return precondition;
        request = request with { ExpectedVersion = expectedVersion };
        var result = await service.CloseAsync(caseId, ActorId(context), request, cancellationToken);
        if (result.IsSuccess)
            SetEtag(context, result.Value!.Version);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : Problem(result.Failure!);
    }

    internal static async Task<Results<Ok<IReadOnlyList<AuditEntryDetails>>, ProblemHttpResult>> GetAudit(
        Guid caseId,
        int? pageSize,
        [FromServices] CaseQueries service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAuditAsync(caseId, pageSize ?? 100, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : Problem(result.Failure!);
    }

    internal static async Task<Results<Ok<LegalHoldDetails>, ProblemHttpResult>> PlaceLegalHold(
        Guid caseId,
        LegalHoldRequest request,
        HttpContext context,
        [FromServices] LegalHoldCommands service,
        CancellationToken cancellationToken)
    {
        if (!TryExpectedVersion(context, out var expectedVersion, out var precondition))
            return precondition;
        request = request with { ExpectedVersion = expectedVersion };
        var result = await service.PlaceLegalHoldAsync(
            caseId,
            ActorId(context),
            request,
            cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : Problem(result.Failure!);
    }

    internal static async Task<Results<Ok<LegalHoldDetails>, ProblemHttpResult>> ReleaseLegalHold(
        Guid caseId,
        LegalHoldRequest request,
        HttpContext context,
        [FromServices] LegalHoldCommands service,
        CancellationToken cancellationToken)
    {
        if (!TryExpectedVersion(context, out var expectedVersion, out var precondition))
            return precondition;
        request = request with { ExpectedVersion = expectedVersion };
        var result = await service.ReleaseLegalHoldAsync(
            caseId,
            ActorId(context),
            request,
            cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : Problem(result.Failure!);
    }

    internal static async Task<Results<Ok<ModerationDecisionExportPageDto>, ProblemHttpResult>>
        ExportDecisionState(
            string? cursor,
            int? pageSize,
            [FromServices] ExportModerationDecisionStateQuery query,
            CancellationToken cancellationToken)
    {
        var result = await query.HandleAsync(cursor, pageSize, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : Problem(result.Failure!);
    }

    private static string ActorId(HttpContext context) =>
        context.User.FindFirstValue("sub")
        ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("An authenticated subject claim is required.");

    private static ProblemHttpResult Problem(ModerationFailure failure)
    {
        var status = failure.Kind switch
        {
            ModerationFailureKind.Validation => StatusCodes.Status400BadRequest,
            ModerationFailureKind.NotFound => StatusCodes.Status404NotFound,
            ModerationFailureKind.Forbidden => StatusCodes.Status403Forbidden,
            ModerationFailureKind.VersionConflict => StatusCodes.Status412PreconditionFailed,
            ModerationFailureKind.Conflict or ModerationFailureKind.IdempotencyConflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        Dictionary<string, object?>? extensions = failure.CurrentVersion.HasValue
            ? new Dictionary<string, object?> { ["currentVersion"] = failure.CurrentVersion.Value }
            : null;
        return TypedResults.Problem(
            statusCode: status,
            title: failure.Code,
            detail: failure.Message,
            extensions: extensions);
    }

    private static bool TryExpectedVersion(
        HttpContext context,
        out long expectedVersion,
        [NotNullWhen(false)] out ProblemHttpResult? problem)
    {
        expectedVersion = 0;
        var values = context.Request.Headers.IfMatch;
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
        {
            problem = TypedResults.Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "if-match-required",
                detail: "Supply one strong If-Match ETag from the latest case representation.");
            return false;
        }

        var value = values[0]!.Trim();
        if (value.StartsWith("W/", StringComparison.OrdinalIgnoreCase)
            || value.Length < 3
            || value[0] != '"'
            || value[^1] != '"'
            || !long.TryParse(value.AsSpan(1, value.Length - 2), out expectedVersion)
            || expectedVersion < 1)
        {
            problem = TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "invalid-if-match",
                detail: "If-Match must be one strong quoted positive case version, for example \"7\".");
            return false;
        }

        problem = null;
        return true;
    }

    private static void SetEtag(HttpContext context, long version) =>
        context.Response.Headers.ETag = $"\"{version}\"";
}
