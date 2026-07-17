using System;

namespace StarterEntities
{
    public interface AnalyticalEntity<TSocial>
        where TSocial : CommomSocialRatingAnalytics
    {
        int Id { get; }
        DateTime CreatedDate { get; }
        TSocial Social { get; set; }
    }
}
