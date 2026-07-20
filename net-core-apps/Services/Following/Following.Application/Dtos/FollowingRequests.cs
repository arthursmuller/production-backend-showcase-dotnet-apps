using Following.Domain.Dtos;

namespace Following.Application.Dtos
{
    /// <summary>
    /// HTTP request data for deciding a friend request. The lookup identifier
    /// stays outside the domain decision payload.
    /// </summary>
    public sealed class ApproveFriendRequestDto : FriendRequestDecisionDomainDto
    {
        public int Id { get; set; }
    }

    /// <summary>
    /// HTTP request data for following a category. The optional external
    /// category identifier is resolved by the application orchestrator.
    /// </summary>
    public sealed class CategoryFollowRequestDto : CategoryFollowDomainDto
    {
        public int? CategoryId { get; set; }
    }

    public static class FollowingRequestMapper
    {
        public static ApproveFriendRequestInput ToApplication(
            this ApproveFriendRequestDto request) =>
            new(
                request.Id,
                new FriendRequestDecisionDomainDto
                {
                    Approve = request.Approve,
                });

        public static AddCategoryFollowInput ToApplication(
            this CategoryFollowRequestDto request) =>
            new(
                request.CategoryId,
                new CategoryFollowDomainDto
                {
                    CategoryName = request.CategoryName,
                });
    }
}
