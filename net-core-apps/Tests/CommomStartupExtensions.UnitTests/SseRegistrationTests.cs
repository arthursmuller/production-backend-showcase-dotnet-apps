using CommomStartupExtensions.Sse;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CommomStartupExtensions.UnitTests;

public sealed class SseRegistrationTests
{
    [Fact]
    public void AddSseHub_registers_one_shared_local_singleton_without_a_hosted_broker_service()
    {
        var services = new ServiceCollection();

        services.AddSseHub();
        services.AddSseHub();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(LocalSseHub));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISseHub));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IHostedService));

        using var provider = services.BuildServiceProvider();
        var concrete = provider.GetRequiredService<LocalSseHub>();
        var abstraction = provider.GetRequiredService<ISseHub>();

        Assert.Same(concrete, abstraction);
    }

    [Fact]
    public void AddSseHub_preserves_an_explicit_ISseHub_override()
    {
        var services = new ServiceCollection();
        var replacement = new StubSseHub();
        services.AddSingleton<ISseHub>(replacement);

        services.AddSseHub();

        using var provider = services.BuildServiceProvider();
        Assert.Same(replacement, provider.GetRequiredService<ISseHub>());
    }

    private sealed class StubSseHub : ISseHub
    {
        public Task Publish(
            string channel,
            int userId,
            object data,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public ISseSubscription Subscribe(string channel, int userId, CancellationToken ct) =>
            throw new NotSupportedException();

        public IReadOnlyList<SseSubscriberSnapshot> SnapshotLocalSubscribers() =>
            Array.Empty<SseSubscriberSnapshot>();
    }
}
