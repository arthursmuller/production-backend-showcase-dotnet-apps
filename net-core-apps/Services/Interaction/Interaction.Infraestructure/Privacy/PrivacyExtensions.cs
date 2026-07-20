using Interaction.Application.Providers.Privacy;
using Interaction.Infraestructure.Configuration;
using InspacyApiHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Interaction.Infraestructure.Privacy;

public static class PrivacyExtensions
{
    public static IServiceCollection ConfigurePrivacy(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var privacyOptions = services.AddOptions<InteractionPrivacyOptions>()
            .Bind(configuration.GetSection(InteractionPrivacyOptions.SectionName));
        if (RuntimeIdentity.Resolve(configuration).ExposesBusinessRoutes())
        {
            privacyOptions.ValidateOnStart();
        }

        services.AddSingleton<IValidateOptions<InteractionPrivacyOptions>, InteractionPrivacyOptionsValidator>();
        services.AddSingleton<IObserverKeyFactory, HmacObserverKeyFactory>();
        return services;
    }
}
