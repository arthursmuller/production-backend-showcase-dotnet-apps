using Comment.Application.Abstractions;
using Comment.Application.Abstractions.Persistence;
using Comment.Application.Extensions;
using Comment.Domain.AggregatesModel;
using StarterEntities;
using System.Threading.Tasks;

namespace Comment.Application.Features.SocialInteractions.Commands
{
    public record ReportCommand(
        ICommentSocialRepository _social,
        IIdentity _auth) : Command<CommomReportDto, CommentSocialAnalytics>
    {
        public override async Task<CommentSocialAnalytics> Handle(CommomReportDto dto)
        {
            var rating = await _social.ExecuteWithConcurrencyRetryAsync(async () =>
            {
                var current = await _social.GetForReportsAsync(dto.Id, _auth.User.Id);
                current.AddReport(new(_auth.User.ToDomainUser(), dto.Reason, current.CommentId));
                await _social.SaveChangesAsync();
                return current;
            });

            return rating.Analytics;
        }
    }
}
