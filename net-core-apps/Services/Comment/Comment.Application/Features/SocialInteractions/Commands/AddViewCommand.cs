using Comment.Application.Abstractions;
using Comment.Application.Abstractions.Persistence;
using Comment.Domain.AggregatesModel;
using StarterEntities;
using System.Threading.Tasks;

namespace Comment.Application.Features.SocialInteractions.Commands
{
    public record AddViewCommand(
        ICommentSocialRepository _social,
        IUserAnalytics _user) : Command<AddViewDto, bool>
    {
        public override async Task<bool> Handle(AddViewDto dto)
        {
            var post = await _social.ExecuteWithConcurrencyRetryAsync(async () =>
            {
                var current = await _social.GetPostAsync(dto.Id);
                if (current is null) return null;

                current.Social.TotalViews += 1;
                await _social.SaveChangesAsync();
                return current;
            });

            if (post is null) return false;

            _user.UpdateViews(post.User.Id, true);

            return true;
        }
    }
}
