using System;
using UserProvider.Dtos;

namespace UserProvider.Abstractions
{
    public interface IBroker
    {
        Guid UpdateAnalytics(UserUpdateAnalytics dto);
    }
}
