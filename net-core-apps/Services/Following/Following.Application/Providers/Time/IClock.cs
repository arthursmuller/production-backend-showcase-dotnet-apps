using System;

namespace Following.Application.Providers.Time
{
    public interface IClock
    {
        DateTime UtcNow { get; }
    }
}
