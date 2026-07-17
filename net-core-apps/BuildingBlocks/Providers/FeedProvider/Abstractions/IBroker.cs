using System;
using FeedProvider.Dtos;

namespace FeedProvider.Abstractions
{
    public interface IBroker
    {
        Guid Add(UserConnectionsFeedDto dto);
        Guid Remove(string entityType, string entityId);
        Guid ChangeVisibility(string entityType, string entityId, bool isPublic);
    }
}
