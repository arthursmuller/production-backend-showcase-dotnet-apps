using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Following.Domain.AggregatesModel;
using Following.Application.Dtos;
using Following.Infra.Persistence;
using IdentityProvider;
using Microsoft.EntityFrameworkCore;

namespace Following.API.Features.Followings
{
    public record CheckCategoryFollowQuery(FollowingContext _context, IIdentityService _auth) : Query<CheckCategoryFollowDto, IEnumerable<CheckCategoryFollowResDto>>
    {
        public override async Task<IEnumerable<CheckCategoryFollowResDto>> Handle(CheckCategoryFollowDto dto)
        {
            List<CategoryFollow> follows = new();

            if (dto.Ids?.Count() > 0)
            {
                var existing = await _context.CategoryFollows
                    .Where(e => dto.Ids.Contains(e.Followed.Id) && e.User.Id == _auth.User.Id)
                    .ToListAsync();

                if (existing is not null)
                    follows.AddRange(existing);
            }
            if (dto.Names?.Count() > 0)
            {
                var existing = await _context.CategoryFollows
                    .Where(e => dto.Names.Contains(e.Followed.Name) && e.User.Id == _auth.User.Id)
                    .ToListAsync();

                if (existing is not null)
                    follows.AddRange(existing);
            }

            return DtoConversor.Convert(dto, follows);
        }
    }
}
