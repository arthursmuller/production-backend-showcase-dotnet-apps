using Comment.Application.Abstractions;
using Comment.Application.Abstractions.Persistence;
using Comment.Domain.AggregatesModel;
using StarterEntities;
using System.Linq;
using System.Threading.Tasks;

namespace Comment.Application.Features.SocialInteractions.Commands
{
    public record ReportAddCommentCommand(
        ICommentSocialRepository _social,
        IIdentity _auth) : Command<ReportAddCommentDto, CommentSocialAnalytics>
    {
        public override async Task<CommentSocialAnalytics> Handle(ReportAddCommentDto dto)
        {
            var rating = await _social.GetForReportsAsync(dto.Id, _auth.User.Id);
            var existingReport = rating?.Reports?.FirstOrDefault(report => report.Id == dto.ReportId);
            if (existingReport is null) return rating?.Analytics;

            existingReport.AddReply(dto.Reply);
            await _social.SaveChangesAsync();

            return rating.Analytics;
        }
    }
}
