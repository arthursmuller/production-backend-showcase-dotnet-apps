using CommomStartupExtensions;
using Following.Application.Dtos;
using Following.Application.Features.Followings;
using Following.API.Features.Followings;
using Following.Domain.AggregatesModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using StarterEntities;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Following.API.AppInterfaces.Routes
{
    public static class FollowingsApiEndpoints
    {
        public static IEndpointRouteBuilder MapFollowingsRoutes(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/followings/v1").WithTags("Followings")
                              .RequireAuthorization(AuthPolicies.FromReactApp);

            // friend requests
            group.MapGet("/friend/request/pending/{id:int}", PendingRequest);
            group.MapPost("/friend/request", RequestFriendship);
            group.MapPost("/friend/request/approve", Approve);
            group.MapPost("/friend/request/list", RequestList);
            group.MapDelete("/friend/request/{id:int}", RemoveRequest);

            // friendships
            group.MapPost("/friend/list", FriendsList);
            group.MapDelete("/friend/{id:int}", RemoveFriend);

            // user follows
            group.MapPost("/user/follow", Follow);
            group.MapPost("/user/follow/list", FollowList);
            group.MapPost("/user/follow/check", FollowCheck);
            group.MapDelete("/user/follow/{id:int}", RemoveFollowing);

            // category follows
            group.MapPost("/category/follow", AddCategoryFollow);
            group.MapPost("/category/follow/list", CategoryFollowList);
            group.MapPost("/category/follow/check", CategoryFollowCheck);
            group.MapDelete("/category/follow", RemoveCategoryFollow);

            // analytics + relations (legacy duplicates)
            group.MapGet("/analytics/{id:int}", GetAnalytics).AllowAnonymous();
            group.MapGet("/relations/get-followers-and-friends-ids/{id:int}", GetUserFollowersAndFriendsIds)
                 .RequireAuthorization(AuthPolicies.ClientReadAccess);
            group.MapGet("/relations/{id:int}", GetRelations).AllowAnonymous();

            return routes;
        }
        
        internal static async Task<IResult> PendingRequest(int id, [FromServices] GetPendingRequestsQuery h) => Results.Ok(R.Success(await h.Handle(id)));
        internal static async Task<IResult> RequestFriendship(RequestFriendshipDto dto, [FromServices] AddFriendRequestCommand h) => Results.Ok(R.Success(await h.Handle(dto)));
        internal static async Task<IResult> Approve(ApproveDto dto, [FromServices] ApproveFriendRequestCommand h) => Results.Ok(R.Success(await h.Handle(dto)));
        internal static async Task<IResult> RequestList(FriendsRequestCriteria criteria, [FromServices] ListFriendRequestsQuery h)
        {
            var (items, total) = await h.Handle(criteria);
            return Results.Ok(PaginationFactory.CreatePagedReponse(items, criteria.PageNumber ?? 1, criteria.PageSize ?? 10, total));
        }
        internal static async Task<IResult> RemoveRequest(int id, [FromServices] RemoveFriendRequestCommand h) => Results.Ok(R.Success(await h.Handle(id)));
        internal static async Task<IResult> FriendsList(FriendsCriteria criteria, [FromServices] ListFriendsQuery h)
        {
            return Results.Ok(PaginationFactory.CreatePagedReponse(await h.Handle(criteria), criteria.PageNumber, criteria.PageSize));
        }
        internal static async Task<IResult> RemoveFriend(int id, [FromServices] RemoveFriendCommand h) => Results.Ok(R.Success(await h.Handle(id)));
        internal static async Task<IResult> Follow(FollowDto dto, [FromServices] AddFollowCommand h) => Results.Ok(R.Success(await h.Handle(dto)));
        internal static async Task<IResult> FollowList(FollowsCriteria criteria, [FromServices] ListFollowsQuery h)
        {
            var (items, total) = await h.Handle(criteria);
            return Results.Ok(PaginationFactory.CreatePagedReponse(items, criteria.PageNumber ?? 1, criteria.PageSize ?? 10, total));
        }
        internal static async Task<IResult> FollowCheck(CheckUserFollowDto dto, [FromServices] CheckUserFollowQuery h) => Results.Ok(R.Success(await h.Handle(dto)));
        internal static async Task<IResult> RemoveFollowing(int id, [FromServices] RemoveFollowingCommand h) => Results.Ok(R.Success(await h.Handle(id)));
        internal static async Task<IResult> AddCategoryFollow(CategoryFollowDto dto, [FromServices] AddCategoryFollowCommand h) => Results.Ok(R.Success(await h.Handle(dto)));
        internal static async Task<IResult> CategoryFollowList(CategoryFollowsCriteria criteria, [FromServices] ListCategoryFollowsQuery h)
        {
            var (items, total) = await h.Handle(criteria);
            return Results.Ok(PaginationFactory.CreatePagedReponse(items, criteria.PageNumber ?? 1, criteria.PageSize ?? 10, total));
        }
        internal static async Task<IResult> CategoryFollowCheck(CheckCategoryFollowDto dto, [FromServices] CheckCategoryFollowQuery h) => Results.Ok(R.Success(await h.Handle(dto)));
        internal static async Task<IResult> RemoveCategoryFollow([FromQuery] int id, [FromQuery] string name, [FromServices] RemoveCategoryFollowCommand h) => Results.Ok(R.Success(await h.Handle(new RemoveCategoryFollowDto { Id = id, Name = name })));
        internal static async Task<IResult> GetAnalytics(int id, [FromServices] GetUserAnalyticsQuery h) => Results.Ok(R.Success(await h.Handle(id)));
        internal static async Task<IResult> GetUserFollowersAndFriendsIds(int id, [FromServices] GetUserFollowersAndFriendsIdsQuery h) => Results.Ok(R.Success(await h.Handle(id)));
        internal static async Task<IResult> GetRelations(int id, [FromServices] GetRelationsQuery h) => Results.Ok(R.Success(await h.Handle(id)));
    }
}
