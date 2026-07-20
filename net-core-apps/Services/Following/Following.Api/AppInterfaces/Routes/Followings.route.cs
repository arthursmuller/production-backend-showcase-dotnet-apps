using Following.Application.Dtos;
using Following.Application.Features.Followings;
using CommomStartupExtensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Following.API.AppInterfaces.Routes
{
    public static class FollowingsApiEndpoints
    {
        public static IEndpointRouteBuilder MapFollowingsRoutes(this IEndpointRouteBuilder routes)
        {
            var cursorGroup = routes.MapGroup("/api/followings")
                .WithTags("Followings")
                .RequireAuthorization(AuthPolicies.FromReactApp);
            cursorGroup.MapGet("/relationships/{id:int}", GetRelations)
                .WithName("GetFollowingRelationship")
                .WithSummary("Returns the authenticated user's relationship state with one target user.")
                .Produces<GetRelationsDto>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound);
            cursorGroup.MapPost("/friend/request", RequestFriendship)
                .WithName("CreateFriendRequest")
                .Accepts<RequestFriendshipDto>("application/json")
                .Produces<FollowingMutationReceiptDto>(StatusCodes.Status201Created)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status409Conflict);
            cursorGroup.MapPost("/friend/request/approve", ApproveFriendRequest)
                .WithName("ApproveFriendRequest")
                .Accepts<ApproveFriendRequestDto>("application/json")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict);
            cursorGroup.MapDelete("/friend/request/{id:int}", RemoveRequest)
                .WithName("DeleteFriendRequest")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound);
            cursorGroup.MapDelete("/friend/{id:int}", RemoveFriend)
                .WithName("DeleteFriend")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound);
            cursorGroup.MapPost("/user/follow", Follow)
                .WithName("CreateUserFollow")
                .Accepts<FollowDto>("application/json")
                .Produces<FollowingMutationReceiptDto>(StatusCodes.Status201Created)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status409Conflict);
            cursorGroup.MapDelete("/user/follow/{id:int}", RemoveFollowing)
                .WithName("DeleteUserFollow")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound);
            cursorGroup.MapPost("/category/follow", AddCategoryFollow)
                .WithName("CreateCategoryFollow")
                .Accepts<CategoryFollowRequestDto>("application/json")
                .Produces<FollowingMutationReceiptDto>(StatusCodes.Status201Created)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status409Conflict);
            cursorGroup.MapGet("/category/follow", GetCategoryFollow)
                .WithName("GetCategoryFollow")
                .Produces<CheckCategoryFollowResDto>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);
            cursorGroup.MapDelete("/category/follow", RemoveCategoryFollow)
                .WithName("DeleteCategoryFollow")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound);
            cursorGroup.MapPost("/friend/request/list", RequestListCursor)
                .WithSummary("Lists one exact friend-request status using an opaque keyset cursor.")
                .Produces<FollowingCursorPage<FriendRequestCursorItemDto>>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status400BadRequest);
            cursorGroup.MapPost("/friend/list", FriendsListCursor)
                .WithSummary("Lists friendships using a stable descending keyset cursor.")
                .Produces<FollowingCursorPage<FriendshipCursorItemDto>>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status400BadRequest);
            cursorGroup.MapPost("/user/follow/list", FollowListCursor)
                .WithSummary("Lists incoming or outgoing follows using a stable keyset cursor.")
                .Produces<FollowingCursorPage<FollowCursorItemDto>>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status400BadRequest);
            cursorGroup.MapPost("/category/follow/list", CategoryFollowListCursor)
                .WithSummary("Lists category follows using a stable keyset cursor.")
                .Produces<FollowingCursorPage<CategoryFollowCursorItemDto>>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status400BadRequest);
            cursorGroup.MapPut("/blocks/{id:int}", BlockUser)
                .WithName("BlockFollowingUser")
                .WithSummary("Blocks a user and atomically revokes both directions of relationship access.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status409Conflict);
            cursorGroup.MapDelete("/blocks/{id:int}", UnblockUser)
                .WithName("UnblockFollowingUser")
                .WithSummary("Removes the authenticated user's directional block without restoring relationships.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status400BadRequest);
            cursorGroup.MapGet("/blocks", ListUserBlocks)
                .WithName("ListFollowingUserBlocks")
                .WithSummary("Lists only the authenticated user's directional blocks using a bounded cursor.")
                .Produces<FollowingCursorPage<UserBlockCursorItemDto>>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status400BadRequest);

            return routes;
        }

        internal static async Task<IResult> GetRelations(
            int id,
            [FromServices] GetRelationsQuery handler,
            CancellationToken cancellationToken) =>
            Results.Ok(await handler.Handle(id, cancellationToken));

        internal static async Task<IResult> RequestFriendship(
            RequestFriendshipDto dto,
            [FromServices] AddFriendRequestCommand handler,
            CancellationToken cancellationToken)
        {
            var created = await handler.Handle(dto, cancellationToken);
            return Results.Created(
                $"/api/followings/relationships/{dto.UserId}",
                new FollowingMutationReceiptDto(
                    "friend-request",
                    dto.UserId,
                    created.Id));
        }

        internal static async Task<IResult> ApproveFriendRequest(
            ApproveFriendRequestDto dto,
            [FromServices] ApproveFriendRequestCommand handler,
            CancellationToken cancellationToken)
        {
            await handler.Handle(dto.ToApplication(), cancellationToken);
            return Results.NoContent();
        }

        internal static async Task<IResult> Follow(
            FollowDto dto,
            [FromServices] AddFollowCommand handler,
            CancellationToken cancellationToken)
        {
            var created = await handler.Handle(dto, cancellationToken);
            return Results.Created(
                $"/api/followings/relationships/{dto.UserId}",
                new FollowingMutationReceiptDto(
                    "follow",
                    dto.UserId,
                    created.Id));
        }

        internal static async Task<IResult> AddCategoryFollow(
            CategoryFollowRequestDto dto,
            [FromServices] AddCategoryFollowCommand handler,
            CancellationToken cancellationToken)
        {
            var created = await handler.Handle(dto.ToApplication(), cancellationToken);
            var categoryName = created.CategoryName
                ?? throw new InvalidOperationException(
                    "A newly created category follow must have a category name.");
            var location = "/api/followings/category/follow?id=" +
                created.CategoryId.ToString(CultureInfo.InvariantCulture) +
                "&name=" + Uri.EscapeDataString(categoryName);
            return Results.Created(
                location,
                new FollowingMutationReceiptDto(
                    "category-follow",
                    created.CategoryId,
                    created.Id));
        }

        internal static async Task<IResult> GetCategoryFollow(
            [FromQuery] int id,
            [FromQuery] string name,
            [FromServices] CheckCategoryFollowQuery handler,
            CancellationToken cancellationToken)
        {
            var status = await handler.Handle(
                new CheckCategoryFollowDto { Ids = [id], Names = [name] },
                cancellationToken);
            return Results.Ok(System.Linq.Enumerable.First(status));
        }

        internal static Task<IResult> RemoveRequest(
            int id,
            [FromServices] RemoveFriendRequestCommand handler,
            CancellationToken cancellationToken) =>
            NoContentAfter(handler.Handle(id, cancellationToken));

        internal static Task<IResult> RemoveFriend(
            int id,
            [FromServices] RemoveFriendCommand handler,
            CancellationToken cancellationToken) =>
            NoContentAfter(handler.Handle(id, cancellationToken));

        internal static Task<IResult> RemoveFollowing(
            int id,
            [FromServices] RemoveFollowingCommand handler,
            CancellationToken cancellationToken) =>
            NoContentAfter(handler.Handle(id, cancellationToken));

        internal static Task<IResult> RemoveCategoryFollow(
            [FromQuery] int id,
            [FromQuery] string name,
            [FromServices] RemoveCategoryFollowCommand handler,
            CancellationToken cancellationToken) =>
            NoContentAfter(handler.Handle(
                new RemoveCategoryFollowDto { Id = id, Name = name },
                cancellationToken));

        private static async Task<IResult> NoContentAfter(Task<bool> operation)
        {
            await operation;
            return Results.NoContent();
        }

        internal static async Task<IResult> RequestListCursor(
            FriendRequestsCursorCriteria criteria,
            [FromServices] ListFriendRequestsCursorQuery query,
            CancellationToken cancellationToken) =>
            Results.Ok(await query.Handle(criteria, cancellationToken));

        internal static async Task<IResult> FriendsListCursor(
            FriendsCursorCriteria criteria,
            [FromServices] ListFriendsCursorQuery query,
            CancellationToken cancellationToken) =>
            Results.Ok(await query.Handle(criteria, cancellationToken));

        internal static async Task<IResult> FollowListCursor(
            FollowsCursorCriteria criteria,
            [FromServices] ListFollowsCursorQuery query,
            CancellationToken cancellationToken) =>
            Results.Ok(await query.Handle(criteria, cancellationToken));

        internal static async Task<IResult> CategoryFollowListCursor(
            CategoryFollowsCursorCriteria criteria,
            [FromServices] ListCategoryFollowsCursorQuery query,
            CancellationToken cancellationToken) =>
            Results.Ok(await query.Handle(criteria, cancellationToken));

        internal static async Task<IResult> BlockUser(
            int id,
            [FromServices] BlockUserCommand command,
            CancellationToken cancellationToken)
        {
            await command.Handle(id, cancellationToken);
            return Results.NoContent();
        }

        internal static async Task<IResult> UnblockUser(
            int id,
            [FromServices] UnblockUserCommand command,
            CancellationToken cancellationToken)
        {
            await command.Handle(id, cancellationToken);
            return Results.NoContent();
        }

        internal static async Task<IResult> ListUserBlocks(
            [FromQuery] int? pageSize,
            [FromQuery] string cursor,
            [FromServices] ListUserBlocksCursorQuery query,
            CancellationToken cancellationToken) =>
            Results.Ok(await query.Handle(
                new UserBlocksCursorCriteria
                {
                    PageSize = pageSize ?? 25,
                    Cursor = cursor,
                },
                cancellationToken));

    }
}
