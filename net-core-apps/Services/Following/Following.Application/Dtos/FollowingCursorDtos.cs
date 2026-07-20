using Following.Domain.AggregatesModel;
using Following.Domain.Repositories;
using System;
using System.Collections.Generic;

namespace Following.Application.Dtos
{
    public abstract class FollowingCursorCriteria
    {
        public int PageSize { get; set; } = 25;
        public string? Cursor { get; set; }
    }

    public sealed class FriendRequestsCursorCriteria : FollowingCursorCriteria
    {
        public bool SentRequests { get; set; }
        public FriendRequestStatus Status { get; set; } = FriendRequestStatus.Pending;
    }

    public sealed class FriendsCursorCriteria : FollowingCursorCriteria
    {
        public int? UserId { get; set; }
    }

    public sealed class FollowsCursorCriteria : FollowingCursorCriteria
    {
        public int UserId { get; set; }
        public bool Following { get; set; }
    }

    public sealed class CategoryFollowsCursorCriteria : FollowingCursorCriteria
    {
        public int UserId { get; set; }
    }

    public sealed class UserBlocksCursorCriteria : FollowingCursorCriteria;

    public sealed record RelationIdsCursorInput(
        int UserId,
        RelationIdKind Kind,
        int PageSize,
        string? Cursor,
        string ActorScope);

    public sealed record FollowingCursorPage<T>(
        IReadOnlyList<T> Items,
        string? NextCursor,
        bool HasMore);

    public sealed record FriendRequestCursorItemDto(
        int Id,
        int SenderUserId,
        string? SenderDisplayName,
        int RecipientUserId,
        string? RecipientDisplayName,
        FriendRequestStatus Status,
        DateTime CreatedDateUtc);

    public sealed record FriendshipCursorItemDto(
        int Id,
        int UserId,
        int FriendUserId,
        string? FriendDisplayName,
        DateTime CreatedDateUtc);

    public sealed record FollowCursorItemDto(
        int Id,
        int FollowerUserId,
        string? FollowerDisplayName,
        int FollowedUserId,
        string? FollowedDisplayName,
        bool IsFollowedBack,
        DateTime CreatedDateUtc);

    public sealed record CategoryFollowCursorItemDto(
        int Id,
        int UserId,
        int CategoryId,
        string? CategoryName,
        DateTime CreatedDateUtc);

    public sealed record UserBlockCursorItemDto(
        int BlockedUserId,
        DateTime CreatedDateUtc);

    public sealed record RelationIdCursorItemDto(
        int RelationId,
        DateTime CreatedDateUtc);
}
