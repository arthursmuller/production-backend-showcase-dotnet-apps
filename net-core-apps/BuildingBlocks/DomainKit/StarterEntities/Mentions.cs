using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StarterEntities
{
    /// <summary>
    /// Represents a single user mention attached to a post-like aggregate
    /// (offer configurations, threads, comments, shares). Persisted via EF
    /// <c>OwnsMany</c> so each parent gets its own dedicated child table
    /// without a cross-context discriminator.
    /// </summary>
    public class UserMention
    {
        public int UserId { get; set; }
        public string UserName { get; set; }

        public UserMention() { }
        public UserMention(int userId, string userName)
        {
            UserId = userId;
            UserName = userName;
        }
    }

    /// <summary>
    /// Helper used by aggregates to merge the mentions list provided by the
    /// caller against any usernames present in the description. Only mentions
    /// whose <c>@username</c> token actually appears in the description text
    /// are kept, eliminating stale entries when the user removes a tag from
    /// the text.
    /// </summary>
    public static class UserMentionsHelper
    {
        // Matches @username (letters, digits, underscore, dot, hyphen).
        private static readonly Regex MentionTokenRegex = new(
            @"(?<![A-Za-z0-9_])@([A-Za-z0-9_][A-Za-z0-9_\.\-]{0,49})",
            RegexOptions.Compiled);

        /// <summary>
        /// Extracts the distinct <c>@username</c> tokens (without the leading
        /// '@') from <paramref name="description"/>. Comparison is case-insensitive.
        /// </summary>
        public static IReadOnlyList<string> ExtractUserNames(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return Array.Empty<string>();
            return MentionTokenRegex.Matches(description)
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Returns the sanitised list of mentions for a post: keeps only those
        /// whose <c>UserName</c> is still present in the description. Avoids
        /// duplicates by <c>UserId</c>.
        /// </summary>
        public static List<UserMention> Sanitize(string description, IEnumerable<UserMention> incoming)
        {
            var result = new List<UserMention>();
            if (incoming == null) return result;

            var present = new HashSet<string>(ExtractUserNames(description), StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<int>();

            foreach (var m in incoming)
            {
                if (m == null) continue;
                if (m.UserId <= 0) continue;
                if (string.IsNullOrWhiteSpace(m.UserName)) continue;
                if (!present.Contains(m.UserName)) continue;
                if (!seen.Add(m.UserId)) continue;
                result.Add(new UserMention(m.UserId, m.UserName));
            }

            return result;
        }
    }
}
