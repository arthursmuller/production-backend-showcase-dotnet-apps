using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using InspacyApiHost;
using Moderation.Application.Features.Cases.Commands;
using Moderation.Application.Features.Cases.Queries;
using Moderation.Application.Features.Maintenance.Commands;
using Moderation.Application.Features.Privacy;
using Moderation.Application.Features.Privacy.Commands;
using Moderation.Application.Features.Reports.Commands;
using Moderation.Application.Features.Reports.Queries;
using Moderation.Application.Options;

namespace Moderation.Application;

public static class ModerationApplicationExtensions
{
    public static IServiceCollection ConfigureApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (!RuntimeIdentity.Resolve(configuration).RunsMigrations())
        {
            services.AddOptions<ModerationPrivacyOptions>()
                .Bind(configuration.GetSection(ModerationPrivacyOptions.SectionName))
                .Validate(
                    ModerationPrivacyOptions.Validate,
                    "Moderation privacy requires a base64 subject lookup key of at least 32 bytes.")
                .ValidateOnStart();
            services.AddSingleton<ModerationSubjectPseudonymizer>();
        }
        services.AddOptions<ModerationErasureOptions>()
            .Bind(configuration.GetSection(ModerationErasureOptions.SectionName))
            .Validate(ModerationErasureOptions.Validate, "Moderation erasure configuration is invalid.")
            .ValidateOnStart();
        services.AddOptions<ModerationRetentionOptions>()
            .Bind(configuration.GetSection(ModerationRetentionOptions.SectionName))
            .Validate(ModerationRetentionOptions.Validate, "Moderation retention configuration is invalid.")
            .ValidateOnStart();
        services.AddOptions<ModerationDecisionRevisionRetentionOptions>()
            .Bind(configuration.GetSection(ModerationDecisionRevisionRetentionOptions.SectionName))
            .Validate(
                ModerationDecisionRevisionRetentionOptions.Validate,
                "Moderation decision revision retention configuration is invalid.")
            .ValidateOnStart();
        services.AddOptions<ModerationSlaOptions>()
            .Bind(configuration.GetSection(ModerationSlaOptions.SectionName))
            .Validate(ModerationSlaOptions.Validate, "Moderation SLA configuration is invalid.")
            .ValidateOnStart();
        services.AddOptions<ModerationEscalationOptions>()
            .Bind(configuration.GetSection(ModerationEscalationOptions.SectionName))
            .Validate(ModerationEscalationOptions.Validate, "Moderation escalation configuration is invalid.")
            .ValidateOnStart();
        services.AddPlatformTimeProvider();
        services.AddScoped<ReportCommands>();
        services.AddScoped<ReportQueries>();
        services.AddScoped<CaseCommands>();
        services.AddScoped<CaseQueries>();
        services.AddScoped<GetModerationSlaBacklogQuery>();
        services.AddScoped<CaseMessageCommands>();
        services.AddScoped<LegalHoldCommands>();
        services.AddScoped<EscalateOverdueCasesCommand>();
        services.AddScoped<RunModerationRetentionCommand>();
        services.AddScoped<PruneModerationDecisionRevisionsCommand>();
        services.AddScoped<ScheduleModerationUserErasureCommand>();
        services.AddScoped<ProcessModerationUserErasureCommand>();
        return services;
    }
}
