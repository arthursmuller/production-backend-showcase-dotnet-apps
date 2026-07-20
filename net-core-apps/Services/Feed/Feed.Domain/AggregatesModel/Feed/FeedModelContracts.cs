namespace Feed.Domain.AggregatesModel
{
    public static class FeedSchemaLimits
    {
        public const int TypeLength = 64;
        public const int TargetIdLength = 200;
        public const int DescriptionLength = 500;
        public const int EventKindLength = 64;
        public const int AudiencePolicyLength = 32;
        public const int MaterializationModeLength = 16;
        public const int RelationshipKindLength = 32;
        public const int StateLength = 24;
        public const int SourceEventIdLength = 128;
        public const int CursorLength = 512;
        public const int LeaseOwnerLength = 128;
        public const int ErrorCodeLength = 64;
    }

    public static class FeedAudiencePolicy
    {
        public const string Public = "public";
        public const string Followers = "followers";
        public const string Friends = "friends";
        public const string FriendsOfFriends = "friends_of_friends";
        public const string Owner = "owner";

        public static bool IsSupported(string value) =>
            value is Public or Followers or Friends or FriendsOfFriends or Owner;
    }
}
