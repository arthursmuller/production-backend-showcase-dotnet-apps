using Interaction.Application.Dtos;
using Interaction.Application.Features.Engagement.Queries;
using Interaction.Application.Features.Exports.Queries;
using Interaction.Application.Features.InteractionStatus.Queries;
using Interaction.Application.Features.Ratings.Commands;
using Interaction.Application.Features.Reactions.Commands;
using Interaction.Application.Features.Views.Commands;
using Interaction.Domain.AggregatesModel;
using Interaction.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Interaction.Api.AppInterfaces.Routes;

public static class InteractionApiEndpoints
{
    public static IEndpointRouteBuilder MapInteractionRoutes(this IEndpointRouteBuilder routes)
    {
        var resources = routes.MapGroup("/api/resources/{type}/{id}").WithTags("Interaction");
        resources.MapPut("/reaction", SetReaction).RequireAuthorization()
            .WithName("InteractionSetReaction").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        resources.MapDelete("/reaction", RemoveReaction).RequireAuthorization()
            .WithName("InteractionRemoveReaction").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        resources.MapPut("/rating", SetRating).RequireAuthorization()
            .WithName("InteractionSetRating").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        resources.MapDelete("/rating", RemoveRating).RequireAuthorization()
            .WithName("InteractionRemoveRating").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        resources.MapPost("/views", ObserveView).RequireAuthorization()
            .WithName("InteractionObserveView").Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound);
        resources.MapGet("/engagement", GetEngagement).AllowAnonymous()
            .WithName("InteractionGetEngagement").Produces<EngagementSummaryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        routes.MapPost("/api/interactions/status:batch", GetStatuses)
            .WithTags("Interaction").WithName("InteractionGetStatuses")
            .RequireAuthorization().Produces<IReadOnlyList<ViewerInteractionStatusDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        routes.MapGet("/internal/interaction/authoritative-state", ExportAuthoritativeState)
            .WithTags("Interaction authority")
            .WithName("ExportInteractionAuthoritativeState")
            .WithSummary("Exports a bounded, service-only resource and absolute engagement authority page.")
            .RequireAuthorization(InteractionPolicies.AuthoritativeExport)
            .Produces<InteractionAuthoritativeExportPageDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        return routes;
    }

    internal static async Task<IResult> SetReaction(
        string type, string id, ReactionRequest request,
        [FromServices] SetReactionCommand command,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ReactionKind>(request.Kind, true, out var kind) || !Enum.IsDefined(kind))
            throw new InteractionValidationException("Reaction kind must be 'like' or 'dislike'.");
        await command.Handle(
            new SetReactionDto(ResourceRef.Parse(type, id), kind), cancellationToken);
        return Results.NoContent();
    }

    internal static async Task<IResult> RemoveReaction(
        string type, string id,
        [FromServices] RemoveReactionCommand command,
        CancellationToken cancellationToken)
    {
        await command.Handle(ResourceRef.Parse(type, id), cancellationToken);
        return Results.NoContent();
    }

    internal static async Task<IResult> SetRating(
        string type, string id, RatingRequest request,
        [FromServices] SetRatingCommand command,
        CancellationToken cancellationToken)
    {
        if (request.Score is < 1 or > 6)
            throw new InteractionValidationException("Rating score must be between 1 and 6.");
        await command.Handle(
            new SetRatingDto(ResourceRef.Parse(type, id), (byte)request.Score, request.Comment),
            cancellationToken);
        return Results.NoContent();
    }

    internal static async Task<IResult> RemoveRating(
        string type, string id,
        [FromServices] RemoveRatingCommand command,
        CancellationToken cancellationToken)
    {
        await command.Handle(ResourceRef.Parse(type, id), cancellationToken);
        return Results.NoContent();
    }

    internal static async Task<IResult> ObserveView(
        string type, string id,
        [FromServices] ObserveViewCommand command,
        CancellationToken cancellationToken)
    {
        await command.Handle(ResourceRef.Parse(type, id), cancellationToken);
        return Results.Accepted();
    }

    internal static async Task<IResult> GetEngagement(
        string type, string id, HttpContext httpContext,
        [FromServices] GetEngagementQuery query,
        CancellationToken cancellationToken)
    {
        var summary = await query.Handle(ResourceRef.Parse(type, id), cancellationToken);
        httpContext.Response.Headers.CacheControl = "public,max-age=15";
        httpContext.Response.Headers.ETag = $"\"{summary.Version}\"";
        return Results.Ok(summary);
    }

    internal static async Task<IResult> GetStatuses(
        StatusBatchRequest request,
        [FromServices] GetViewerStatusesQuery query,
        CancellationToken cancellationToken)
    {
        if (request.Resources is null)
            throw new InteractionValidationException("Resources are required.");
        var resources = request.Resources.Select(item => ResourceRef.Parse(item.Type, item.Id)).ToArray();
        return Results.Ok(await query.Handle(resources, cancellationToken));
    }

    internal static async Task<IResult> ExportAuthoritativeState(
        string? cursor,
        int? pageSize,
        [FromServices] ExportInteractionAuthoritativeStateQuery query,
        CancellationToken cancellationToken) =>
        Results.Ok(await query.Handle(
            new ExportInteractionAuthoritativeStateRequest(cursor, pageSize),
            cancellationToken));
}

public sealed record ReactionRequest(string Kind);
public sealed record RatingRequest(int Score, string? Comment);
public sealed record StatusResourceRequest(string Type, string Id);
public sealed record StatusBatchRequest(IReadOnlyList<StatusResourceRequest>? Resources);
