using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EfCoreFilters;
using IdentityProvider;
using Microsoft.EntityFrameworkCore;
using Comment.Domain.AggregatesModel;
using Comment.Application.Dtos;
using Comment.Infra.Persistence;
using StarterEntities;

namespace Comment.Infra.Helpers
{
    public static class CommentHelpers
    {
        public static User ToDomainUser(this AuthUser u) => u is null ? null : new User(u.Id, u.Name);

        public static async Task<(IEnumerable<CommentPost>, int)> Filter(
            IQueryable<CommentPost> query, CommentsCriteria criteria)
        {
            var filters = new DbFilters<CommentPost>(query)
                .New(criteria.UserId, (e, val) => e.User.Id == val)
                .New(criteria.RelatedEntityProps?.Select(e => $"{e.Id}{e.EntityType}".ToLower())?.ToArray(),
                     (e, val) => val.Contains(e.RelatedEntity))
                .New(criteria.Ids, (e, val) => val.Contains(e.Id));

            return await filters.Build().Filter(
                criteria,
                overrideSortable: (sort, ordered) => sort switch
                {
                    "trending" => ordered.OrderByTrending<CommentPost, CommentSocialAnalytics>(),
                    _ => ordered,
                }
            );
        }

        public static async Task<List<CheckSocialInteractionsDto>> CheckSocialInteractionsForUser(
            CommentContext context, int[] postIds, int userId)
        {
            var results = await context.CommentLikes
                .AsNoTracking()
                .Where(l => l.User.Id == userId && postIds.Contains(l.PostId))
                .Select(l => new { Type = "Like", l.PostId, PostGenericCode = "" })
                .Union(context.CommentDislikes.AsNoTracking()
                    .Where(d => d.User.Id == userId && postIds.Contains(d.PostId))
                    .Select(d => new { Type = "Dislike", d.PostId, PostGenericCode = "" }))
                .Union(context.CommentRatingLevels.AsNoTracking()
                    .Where(d => d.User.Id == userId && postIds.Contains(d.PostId))
                    .Select(d => new { Type = "Rating", d.PostId, PostGenericCode = "" }))
                .Union(context.CommentReports.AsNoTracking()
                    .Where(d => d.User.Id == userId && postIds.Contains(d.PostId))
                    .Select(d => new { Type = "Report", d.PostId, PostGenericCode = "" }))
                .Union(context.CommentComments.AsNoTracking()
                    .Where(d => d.UserId == userId && postIds.Contains(d.PostId))
                    .Select(d => new { Type = "Comment", d.PostId, PostGenericCode = "" }))
                .Union(context.CommentShares.AsNoTracking()
                    .Where(d => d.UserId == userId && postIds.Contains(d.PostId))
                    .Select(d => new { Type = "Share", d.PostId, PostGenericCode = "" }))
                .Union(context.CommentBookmarks.AsNoTracking()
                    .Where(b => b.UserId == userId && postIds.Contains(b.PostId))
                    .Select(b => new { Type = "Bookmark", b.PostId, PostGenericCode = "" }))
                .AsSplitQuery()
                .ToListAsync();

            return postIds?.Select(id => new CheckSocialInteractionsDto
            {
                PostId = id,
                Liked = results.Any(r => r.Type == "Like" && r.PostId == id),
                Disliked = results.Any(r => r.Type == "Dislike" && r.PostId == id),
                Rated = results.Any(r => r.Type == "Rating" && r.PostId == id),
                Reported = results.Any(r => r.Type == "Report" && r.PostId == id),
                Commented = results.Any(r => r.Type == "Comment" && r.PostId == id),
                Shared = results.Any(r => r.Type == "Share" && r.PostId == id),
                Bookmarked = results.Any(r => r.Type == "Bookmark" && r.PostId == id),
            }).ToList();
        }

        public static async Task<CheckSocialInteractionsDto> CheckSocialInteractionsForUser(
            CommentContext context, int postId, int userId)
        {
            var results = await context.CommentLikes.AsNoTracking()
                .Where(l => l.User.Id == userId && l.PostId == postId)
                .Select(l => new { Type = "Like", l.PostId })
                .Union(context.CommentDislikes.AsNoTracking()
                    .Where(d => d.User.Id == userId && d.PostId == postId)
                    .Select(d => new { Type = "Dislike", d.PostId }))
                .Union(context.CommentRatingLevels.AsNoTracking()
                    .Where(d => d.User.Id == userId && d.PostId == postId)
                    .Select(d => new { Type = "Rating", d.PostId }))
                .Union(context.CommentReports.AsNoTracking()
                    .Where(d => d.User.Id == userId && d.PostId == postId)
                    .Select(d => new { Type = "Report", d.PostId }))
                .Union(context.CommentComments.AsNoTracking()
                    .Where(d => d.UserId == userId && d.PostId == postId)
                    .Select(d => new { Type = "Comment", d.PostId }))
                .Union(context.CommentShares.AsNoTracking()
                    .Where(d => d.UserId == userId && d.PostId == postId)
                    .Select(d => new { Type = "Share", d.PostId }))
                .Union(context.CommentBookmarks.AsNoTracking()
                    .Where(b => b.UserId == userId && b.PostId == postId)
                    .Select(b => new { Type = "Bookmark", b.PostId }))
                .AsSplitQuery()
                .ToListAsync();

            return new CheckSocialInteractionsDto
            {
                PostId = postId,
                Liked = results.Any(r => r.Type == "Like"),
                Disliked = results.Any(r => r.Type == "Dislike"),
                Rated = results.Any(r => r.Type == "Rating"),
                Reported = results.Any(r => r.Type == "Report"),
                Commented = results.Any(r => r.Type == "Comment"),
                Shared = results.Any(r => r.Type == "Share"),
                Bookmarked = results.Any(r => r.Type == "Bookmark"),
            };
        }
    }
}
