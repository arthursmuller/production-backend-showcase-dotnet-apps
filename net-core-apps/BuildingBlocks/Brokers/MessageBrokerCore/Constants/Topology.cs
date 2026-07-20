namespace MessageBrokerCore.Constants;

/// <summary>Durable receive endpoint names owned by the consuming services.</summary>
public static class Topology
{
    public static class Feed
    {
        public static class ActivityChanged
        {
            public const string Queue = "feed_activity_changed_queue";
        }
    }

    public static class Following
    {
        public static class RelationshipStateChanged
        {
            public const string Queue = "feed_following_relationship_state_queue";
        }

        public static class SubscriptionFeatureNetworking
        {
            public const string Queue = "following_subscription_feature_networking_queue";
        }
    }
}
