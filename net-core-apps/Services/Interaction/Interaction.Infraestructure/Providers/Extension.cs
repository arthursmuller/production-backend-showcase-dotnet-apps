using IdentityProvider;
using Interaction.Application.Providers.Events;
using Interaction.Infraestructure.Providers.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Interaction.Infraestructure.Providers;

public static class ProviderExtensions
{
    public static IServiceCollection ConfigureProviders(this IServiceCollection services)
    {
        services.AddScoped<IInteractionEventPublisher, InteractionEventPublisher>();
        services.ConfigureIdentityProvider();
        return services;
    }
}
