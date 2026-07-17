using System;

namespace StarterEntities
{
    public abstract class CommomSubscriptionFeature : BaseEntity
    {
        public int OwnerId { get; set; }
        public int RequesterId { get; set; }
        public string SubscriptionId { get; set; }
        public DateTime EndDate { get; set; }

        protected CommomSubscriptionFeature(
            int ownerId,
            int requesterId,
            string subscriptionId,
            DateTime endDate)
        {
            OwnerId = ownerId;
            RequesterId = requesterId;
            SubscriptionId = subscriptionId;
            EndDate = endDate;
        }

        protected CommomSubscriptionFeature() { }
    }
}
