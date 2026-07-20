using Comment.Domain.Exceptions;
using RichEnumeration;
using System.Collections.Generic;
using System.Linq;

namespace Comment.Domain.AggregatesModel
{
    public sealed class VisibilityLevel : Enumeration
    {
        public string Description { get; }

        public static readonly VisibilityLevel Global = new(
            1,
            nameof(Global).ToLowerInvariant(),
            "Todos");
        public static readonly VisibilityLevel Friends = new(
            2,
            nameof(Friends).ToLowerInvariant(),
            "Amigos");
        public static readonly VisibilityLevel FriendsOfFriends = new(
            3,
            nameof(FriendsOfFriends).ToLowerInvariant(),
            "Amigos de amigos");
        public static readonly VisibilityLevel Private = new(
            4,
            nameof(Private).ToLowerInvariant(),
            "Eu");

        private VisibilityLevel(int id, string name, string description)
            : base(id, name) => Description = description;

        public static IEnumerable<VisibilityLevel> List() =>
            [Global, Friends, FriendsOfFriends, Private];

        public static VisibilityLevel? From(int id) =>
            List().SingleOrDefault(level => level.Id == id);
    }

    public static class CommentAudiencePolicies
    {
        public const string Public = "public";
        public const string Friends = "friends";
        public const string FriendsOfFriends = "friends_of_friends";
        public const string Owner = "owner";

        public static string FromVisibilityIdOrOwner(int visibilityLevelId) =>
            visibilityLevelId switch
            {
                1 => Public,
                2 => Friends,
                3 => FriendsOfFriends,
                4 => Owner,
                _ => Owner,
            };

        public static string NormalizeOrOwner(string? value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            return normalized switch
            {
                Public => Public,
                Friends => Friends,
                FriendsOfFriends => FriendsOfFriends,
                Owner => Owner,
                _ => Owner,
            };
        }

        public static bool IsSupported(string? value) =>
            value is Public or Friends or FriendsOfFriends or Owner;
    }

    internal static class CommentVisibilityRules
    {
        internal static int Normalize(int? value)
        {
            var requestedId = value ?? VisibilityLevel.Private.Id;
            var level = VisibilityLevel.From(requestedId);
            if (level is null)
                throw new CommentException($"Visibility level '{requestedId}' is invalid.");

            return level.Id;
        }

        internal static bool IsPublic(int visibilityLevelId) =>
            visibilityLevelId == VisibilityLevel.Global.Id;
    }
}
