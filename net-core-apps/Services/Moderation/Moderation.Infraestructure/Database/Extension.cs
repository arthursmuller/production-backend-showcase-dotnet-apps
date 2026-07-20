using Inspacy.Data.MariaDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moderation.Application.Providers;
using Moderation.Domain.Repositories;
using Moderation.Infra.Database.Ef;
using Moderation.Infra.Database.Ef.Repositories;
using Moderation.Infra.Providers;

namespace Moderation.Infra.Database;

public static class ModerationInfrastructureExtensions
{
    public static IServiceCollection ConfigureInfraestructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connection = MariaDbConnectionPolicy.Create(configuration, "moderation");
        services.AddMariaDbCommandTelemetry(configuration);

        services.AddDbContextPool<ModerationDbContext>((provider, options) =>
        {
            options.UseMariaDbRuntime(connection, provider);
        },
            poolSize: 128);
        services.AddScoped<ModerationRepository>();
        services.AddScoped<IModerationRepository>(provider =>
            provider.GetRequiredService<ModerationRepository>());
        services.AddScoped<IModerationDecisionExportRepository>(provider =>
            provider.GetRequiredService<ModerationRepository>());
        services.AddScoped<IModerationDecisionRevisionMaintenanceRepository>(provider =>
            provider.GetRequiredService<ModerationRepository>());
        services.AddScoped<IModerationRetentionStore, ModerationRetentionStore>();
        services.AddScoped<IModerationUserErasureStore, ModerationUserErasureStore>();
        services.AddScoped<IModerationEventPublisher, MassTransitModerationEventPublisher>();
        return services;
    }
}
