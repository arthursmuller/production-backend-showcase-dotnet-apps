using Following.Application.Abstractions;
using Following.Application.Dtos;
using Following.Domain.AggregatesModel;
using StarterEntities;
using System;
using System.Threading.Tasks;

namespace Following.Application.Features.Subscriptions
{
    public record SetFriendsSubscriptionFeatureCommand(IFollowingRepository Repository)
        : Command<SubscriptionFeatureUpdatedDto, FriendshipSubscriptionFeature>
    {
        public override async Task<FriendshipSubscriptionFeature> Handle(
            SubscriptionFeatureUpdatedDto dto)
        {
            var entity = await Repository.GetSubscriptionFeature(
                dto.SubscriptionId,
                dto.Requester.Id,
                dto.Owner.Id);

            if (dto.EndDate is null)
            {
                if (entity is null)
                    return null;

                Repository.Remove(entity);
            }
            else if (entity is null)
            {
                entity = new FriendshipSubscriptionFeature(
                    dto.Owner.Id,
                    dto.Requester.Id,
                    dto.SubscriptionId,
                    dto.EndDate.Value);

                await Repository.Add(entity);
                await Repository.AddFriendship(
                    new User(dto.Requester.Id, dto.Requester.Name),
                    new User(dto.Owner.Id, dto.Owner.Name));
            }
            else
            {
                entity.EndDate = dto.EndDate.Value;
            }

            await Repository.SaveChanges();
            return entity;
        }
    }
}
