using Inspacy.Data.MariaDb;
using Interaction.Domain.Repositories;
using Interaction.Infraestructure.Database.Ef;
using Interaction.Infraestructure.Database.Ef.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Interaction.Infraestructure.Database;

public static class DatabaseExtensions
{
    public static IServiceCollection ConfigureDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connection = MariaDbConnectionPolicy.Create(configuration, "interaction");
        services.AddMariaDbCommandTelemetry(configuration);
        services.AddDbContextPool<InteractionDbContext>(
            (provider, options) =>
            {
                options.UseMariaDbRuntime(connection, provider);
            },
            poolSize: 128);
        services.AddScoped<InteractionRepository>();
        services.AddScoped<IInteractionRepository>(provider =>
            provider.GetRequiredService<InteractionRepository>());
        services.AddScoped<IInteractionAuthoritativeExportRepository>(provider =>
            provider.GetRequiredService<InteractionRepository>());
        services.AddScoped<IInteractionAuthorityRevisionMaintenanceRepository>(provider =>
            provider.GetRequiredService<InteractionRepository>());
        services.AddScoped<IInteractionErasureRepository, InteractionErasureRepository>();
        return services;
    }
}
