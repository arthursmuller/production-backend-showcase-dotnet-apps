using Following.Application.Providers.Privacy;
using Following.Infra.Persistence;
using MassTransit;
using MessageBrokerContracts.Privacy.Restricted;
using MessageBrokerExtensions;
using System;
using System.Threading.Tasks;

namespace Following.API.AppInterfaces.Queues
{
    /// <summary>
    /// The restricted consumer only persists/updates the permanent tombstone and schedules the
    /// bounded processor. It never scans relationship tables on the broker delivery thread.
    /// </summary>
    public sealed class UserErasureRequestedConsumer(
        IFollowingUserErasureScheduler scheduler) : IConsumer<UserErasureRequested>
    {
        public async Task Consume(ConsumeContext<UserErasureRequested> context)
        {
            await scheduler.ScheduleAsync(Map(context.Message), context.CancellationToken);
        }

        internal static FollowingUserErasureRequest Map(UserErasureRequested message)
        {
            ArgumentNullException.ThrowIfNull(message);
            if (message.UserId is <= 0 or > int.MaxValue)
                throw new InvalidOperationException("The privacy request contains an unsupported user ID.");

            return new FollowingUserErasureRequest(
                message.EventId,
                message.RequestId,
                checked((int)message.UserId),
                message.ErasureVersion,
                message.RequestedAtUtc.UtcDateTime);
        }
    }

    public sealed class UserErasureRequestedConsumerDefinition
        : EntityFrameworkOutboxConsumerDefinition<UserErasureRequestedConsumer, FollowingContext>
    {
        public UserErasureRequestedConsumerDefinition()
            : base(
                "following_privacy_user_erasure",
                prefetchCount: 8,
                concurrencyLimit: 4)
        {
        }
    }
}
