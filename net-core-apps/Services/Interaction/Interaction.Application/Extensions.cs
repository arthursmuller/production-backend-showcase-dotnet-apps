using Cqrs.Extensions;
using Interaction.Application.Options;
using Interaction.Application.Features.Maintenance.Commands;
using Interaction.Application.Providers.Execution;
using InspacyApiHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Interaction.Application;

public static class ApplicationExtensions
{
    public static IServiceCollection ConfigureApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InteractionOptions>()
            .Bind(configuration.GetSection(InteractionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<InteractionOptions>, InteractionOptionsValidator>();
        services.AddPlatformTimeProvider();
        services.AddScoped<InteractionExecutionContext>();
        services.AddScoped<PruneInteractionAuthorityRevisionsCommand>();
        services.RegisterCQRS(typeof(ApplicationExtensions).Assembly);
        return services;
    }
}
