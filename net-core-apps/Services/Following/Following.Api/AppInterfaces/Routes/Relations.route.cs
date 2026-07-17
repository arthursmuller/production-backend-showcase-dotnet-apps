using System.Threading.Tasks;
using CommomStartupExtensions;
using Following.API.Features.Followings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Following.API.AppInterfaces.Routes
{
    public static class RelationsApiEndpoints
    {
        public static IEndpointRouteBuilder MapRelationsRoutes(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/relations/v1").WithTags("Relations");

            group.MapGet("/get-followers-and-friends-ids/{id:int}", GetUserFollowersAndFriendsIds)
                 .RequireAuthorization(AuthPolicies.ClientReadAccess);

            group.MapGet("/{id:int}", GetRelations)
                 .RequireAuthorization(AuthPolicies.FromReactApp);

            return routes;
        }

        internal static async Task<IResult> GetUserFollowersAndFriendsIds(int id, [FromServices] GetUserFollowersAndFriendsIdsQuery h) 
            => Results.Ok(R.Success(await h.Handle(id)));
        internal static async Task<IResult> GetRelations(int id, [FromServices] GetRelationsQuery h)
            => Results.Ok(R.Success(await h.Handle(id)));
    }
}
