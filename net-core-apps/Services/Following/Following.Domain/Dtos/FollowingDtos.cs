using System;

namespace Following.Domain.Dtos
{
    public class FriendRequestDecisionDomainDto
    {
        public bool Approve { get; set; }
    }

    public class CategoryFollowDomainDto
    {
        public string CategoryName { get; set; } = string.Empty;
    }

    public class SubscriptionFeatureUpdateDomainDto
    {
        public DateTime? EndDate { get; set; }
    }
}
