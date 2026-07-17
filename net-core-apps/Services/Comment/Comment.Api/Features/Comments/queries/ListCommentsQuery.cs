using Comment.Domain.AggregatesModel;
using Comment.Application.Dtos;
using Comment.Infra.Helpers;
using Comment.Infra.Persistence;
using IdentityProvider;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Comment.API.Features.Comments
{
    public record ListCommentsQuery(CommentContext _context, IIdentityService _auth) : Query<CommentsCriteria, (IEnumerable<CommentPostDto>, int)>
    {
        public override async Task<(IEnumerable<CommentPostDto>, int)> Handle(CommentsCriteria req)
        {
            var (list, count) = await CommentHelpers.Filter(
                _context.Comments.Include(e => e.Social).AsQueryable(), req);

            var dtos = CommentConversor.Convert(list);

            if (_auth.User.Id > 0)
            {
                var interactions = await CommentHelpers.CheckSocialInteractionsForUser(
                    _context, list?.Select(e => e.Id)?.ToArray(), _auth.User.Id);

                dtos = dtos.Select(post =>
                {
                    post.SocialInteractionsStatus = interactions.FirstOrDefault(e => e.PostId == post.Id);
                    return post;
                });
            }

            return (dtos, count);
        }
    }
}
