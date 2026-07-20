using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;

namespace Feed.Infra.Persistence
{
    public sealed class FeedPullCandidateRow
    {
        public long EntryId { get; set; }
        public long ActivityId { get; set; }
        public string TargetType { get; set; } = string.Empty;
        public string TargetId { get; set; } = string.Empty;
        public string ActivityKind { get; set; } = string.Empty;
        public int ActorUserId { get; set; }
        public string AudiencePolicy { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public long RankKey { get; set; }
        public DateTime PublishedAtUtc { get; set; }
        public DateTime? SeenAtUtc { get; set; }
        public string MaterializationMode { get; set; } = string.Empty;
        public long FairRow { get; set; }
    }

    public sealed class FeedPullCandidateRowConfiguration
        : IEntityTypeConfiguration<FeedPullCandidateRow>
    {
        public void Configure(EntityTypeBuilder<FeedPullCandidateRow> builder)
        {
            builder.HasNoKey();
            builder.ToView(null);
        }
    }
}
