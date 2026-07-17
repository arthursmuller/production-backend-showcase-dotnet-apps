using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CategoriesProvider.Abstractions;
using EfCoreFilters;
using FeedProvider.Abstractions;
using IdentityProvider;
using Microsoft.EntityFrameworkCore;

using StarterEntities;
using Comment.Domain.AggregatesModel;
using Comment.Application.Dtos;
using Comment.Infra.Helpers;
using Comment.Infra.Persistence;
using PagedCommentDtoResponse = PagedResponse<System.Collections.Generic.IEnumerable<Comment.Application.Dtos.CommentPostDto>>;
using PagedCommentResponse = PagedResponse<System.Collections.Generic.IEnumerable<Comment.Domain.AggregatesModel.CommentPost>>;
namespace Comment.API.Features.Comments
{
    public record GetCommentOwnerByIdQuery(CommentContext _context, IIdentityService _auth) : Query<int, CommentPost>
    {
        public override Task<CommentPost> Handle(int id) =>
            _context.Comments.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id && e.User.Id == _auth.User.Id);
    }
}
