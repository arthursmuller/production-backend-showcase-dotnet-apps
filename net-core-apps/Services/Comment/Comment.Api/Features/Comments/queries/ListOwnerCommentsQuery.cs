using Comment.Domain.AggregatesModel;
using Comment.Application.Dtos;
using Comment.Infra.Helpers;
using Comment.Infra.Persistence;
using IdentityProvider;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Comment.API.Features.Comments
{
    public record ListOwnerCommentsQuery(CommentContext _context, IIdentityService _auth) : Query<CommentsCriteria, (IEnumerable<CommentPost>, int)>
    {
        public override async Task<(IEnumerable<CommentPost>, int)> Handle(CommentsCriteria req)
        {
            return await CommentHelpers.Filter(
                _context.Comments.Where(e => e.User.Id == _auth.User.Id).AsQueryable(), req);
        }
    }
}
