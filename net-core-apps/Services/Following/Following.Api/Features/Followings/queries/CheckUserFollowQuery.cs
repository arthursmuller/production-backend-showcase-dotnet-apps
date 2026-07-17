using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Following.Application.Dtos;
using Following.Infra.Persistence;
using IdentityProvider;
using Microsoft.EntityFrameworkCore;

namespace Following.API.Features.Followings
{
    public record CheckUserFollowQuery(FollowingContext _context, IIdentityService _auth) : Query<CheckUserFollowDto, IEnumerable<CheckUserFollowResDto>>
    {
        public override async Task<IEnumerable<CheckUserFollowResDto>> Handle(CheckUserFollowDto dto)
        {
            var follows = await _context.Follows.Where(e => dto.Ids.Contains(e.Followed.Id) && e.User.Id == _auth.User.Id).ToListAsync();
            return dto.Ids.Select(id => new CheckUserFollowResDto() { Id = id, IsFollowing = follows?.Any(f => f.Followed.Id == id) ?? false });
        }
    }
}
