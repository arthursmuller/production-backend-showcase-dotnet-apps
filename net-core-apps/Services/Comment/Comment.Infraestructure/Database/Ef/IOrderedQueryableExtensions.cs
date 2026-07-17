using System;
using System.Linq;
using System.Data;
using StarterEntities;
using Microsoft.EntityFrameworkCore;

namespace Comment.Infra.Persistence
{
    public static class QueryableScoreExtensions
    {
        public static IQueryable<T> OrderByTrending<T, TAnalytics>(
            this IQueryable<T> query)
                where T : AnalyticalEntity<TAnalytics>
                where TAnalytics : CommomSocialRatingAnalytics
        {

            double viewsWeight = 0.3;
            double likesWeight = 0.3;
            double commentsWeight = 0.5;
            double dislikesWeight = -0.1;
            double recencyWeight = 0.09;

            return query.OrderBy(e =>
                (double)e.Social.TotalViews / int.MaxValue * viewsWeight +
                (double)(e.Social.TotalLikes + e.Social.TotalComments + e.Social.TotalDislikes) / int.MaxValue * (likesWeight + commentsWeight + dislikesWeight) +
                (EF.Functions.DateDiffHour(e.CreatedDate, DateTime.UtcNow) / 24.0) * recencyWeight
            );
        }
    }
  
}