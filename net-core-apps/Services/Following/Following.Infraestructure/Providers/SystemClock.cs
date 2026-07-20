using Following.Application.Providers.Time;
using System;

namespace Following.Infra.Providers
{
    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
