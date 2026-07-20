using System.Threading.Tasks;
using Following.Application.Features.Followings;
using Following.Application.Dtos;
using Following.Domain.Exceptions;
using Following.Domain.Repositories;
using Following.Application.Providers.Relationships;
using MessageBrokerContracts.Following;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System;
using System.Security.Claims;
using System.Threading;

namespace Following.API.AppInterfaces.Routes
{
    public static class RelationsApiEndpoints
    {
        public static IEndpointRouteBuilder MapRelationsRoutes(this IEndpointRouteBuilder routes)
        {
            var cursorGroup = routes.MapGroup("/api/relations")
                .WithTags("Relations")
                .RequireAuthorization(AuthPolicies.ClientReadAccess);
            cursorGroup.MapGet("/users/{id:int}/relation-ids", GetRelationIdsCursor)
                .WithSummary(
                    "Lists one bounded relation-ID kind: friends, followers, or categories.")
                .Produces<FollowingCursorPage<RelationIdCursorItemDto>>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            routes.MapGet(
                    "/api/internal/relationships/snapshot",
                    GetRelationshipSnapshot)
                .WithTags("Internal relationships")
                .WithName("GetRelationshipSnapshot")
                .WithSummary("Returns one bounded authority snapshot page for background reconciliation.")
                .RequireAuthorization(AuthPolicies.ClientReadAccess)
                .RequireAuthorization(policy => policy.RequireAssertion(context =>
                    IsServicePrincipal(context.User)))
                .Produces<RelationshipSnapshotPage>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            return routes;
        }

        internal static async Task<IResult> GetRelationIdsCursor(
            int id,
            [FromQuery] string kind,
            [FromQuery] int? pageSize,
            [FromQuery] string cursor,
            HttpContext httpContext,
            [FromServices] ListRelationIdsCursorQuery query,
            CancellationToken cancellationToken)
        {
            if (!Enum.TryParse<RelationIdKind>(kind, ignoreCase: true, out var parsedKind) ||
                !Enum.IsDefined(parsedKind))
            {
                throw new FollowingException(
                    "Kind must be friends, followers, or categories.");
            }

            var input = new RelationIdsCursorInput(
                id,
                parsedKind,
                pageSize ?? 25,
                cursor,
                GetAuthenticatedClientScope(httpContext.User));
            return Results.Ok(await query.Handle(input, cancellationToken));
        }

        internal static string GetAuthenticatedClientScope(ClaimsPrincipal principal)
        {
            ArgumentNullException.ThrowIfNull(principal);
            var actorClaim = principal.FindFirst("client_id")
                ?? principal.FindFirst("azp")
                ?? principal.FindFirst("sub");
            var actor = actorClaim?.Value?.Trim();
            var issuer = principal.FindFirst("iss")?.Value?.Trim() ?? "configured-issuer";
            if (actorClaim is null
                || string.IsNullOrWhiteSpace(actor)
                || actor.Length > 256
                || issuer.Length > 240)
            {
                throw new FollowingException(
                    "The authenticated client is missing a bounded stable identifier.",
                    StatusCodes.Status403Forbidden);
            }

            return $"{actorClaim.Type}:{actor}|iss:{issuer}";
        }

        internal static bool IsServicePrincipal(ClaimsPrincipal principal)
        {
            if (principal is null)
                return false;
            var clientId = principal.FindFirst("client_id")?.Value?.Trim()
                ?? principal.FindFirst("azp")?.Value?.Trim();
            return !string.IsNullOrWhiteSpace(clientId)
                && clientId.Length <= 256
                && !string.Equals(clientId, "react", StringComparison.Ordinal);
        }

        internal static async Task<IResult> GetRelationshipSnapshot(
            [FromQuery] long afterCursor,
            [FromQuery] int? pageSize,
            [FromQuery] long? snapshotHighWatermark,
            [FromServices] IRelationshipSnapshotReader reader,
            CancellationToken cancellationToken) =>
            Results.Ok(await reader.ReadPageAsync(
                afterCursor,
                pageSize ?? 250,
                snapshotHighWatermark,
                cancellationToken));
    }
}
