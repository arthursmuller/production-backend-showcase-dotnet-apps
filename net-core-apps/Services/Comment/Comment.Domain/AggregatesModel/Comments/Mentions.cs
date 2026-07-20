using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Comment.Domain.AggregatesModel
{
    public class UserMention
    {
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;

        public UserMention() { }

        public UserMention(int userId, string userName)
        {
            UserId = userId;
            UserName = userName;
        }
    }

    internal static class CommentMentionRules
    {
        private static readonly Regex MentionTokenRegex = new(
            @"(?<![A-Za-z0-9_])@([A-Za-z0-9_][A-Za-z0-9_\.\-]{0,49})",
            RegexOptions.Compiled);

        public static List<UserMention> Sanitize(string? description, IEnumerable<UserMention>? incoming)
        {
            if (incoming is null || string.IsNullOrWhiteSpace(description))
                return new List<UserMention>();

            var present = MentionTokenRegex.Matches(description)
                .Select(match => match.Groups[1].Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<int>();

            return incoming
                .Where(mention => mention is not null)
                .Where(mention => mention.UserId > 0)
                .Where(mention => !string.IsNullOrWhiteSpace(mention.UserName))
                .Where(mention => present.Contains(mention.UserName))
                .Where(mention => seen.Add(mention.UserId))
                .Select(mention => new UserMention(mention.UserId, mention.UserName))
                .ToList();
        }
    }
}
