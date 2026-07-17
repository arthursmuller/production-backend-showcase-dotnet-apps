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
    public record GetCommentByIdQuery(CommentContext _context, IIdentityService _auth) : Query<int, CommentPostDto>
    {
        public override async Task<CommentPostDto> Handle(int id)
        {
            var entity = await _context.Comments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
            var existing = CommentConversor.Convert(entity);
            if (_auth.User.Id > 0)
                existing.SocialInteractionsStatus = await CommentHelpers.CheckSocialInteractionsForUser(_context, existing.Id, _auth.User.Id);
            return existing;
        }
    }
}
