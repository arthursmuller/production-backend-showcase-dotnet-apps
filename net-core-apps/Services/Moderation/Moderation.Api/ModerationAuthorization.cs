using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using InspacyApiHost;
using Moderation.Application.Features.Exports;
using Moderation.Application.Features.Exports.Queries;
using Moderation.Application.Options;

namespace Moderation.API;

public static class ModerationPolicies
{
    public const string Reporter = "ModerationReporter";
    public const string Triage = "ModerationTriage";
    public const string Investigator = "ModerationInvestigator";
    public const string Decision = "ModerationDecision";
    public const string Admin = "ModerationAdmin";
    public const string DecisionExport = "ModerationDecisionExport";
}

public static class ModerationAuthorization
{
    public static IServiceCollection AddModerationAuthorization(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var exportScope = configuration?["Identity:DecisionExportScope"]?.Trim();
        if (string.IsNullOrWhiteSpace(exportScope))
            exportScope = "moderation.decision.export";
        services.AddAuthorization(options =>
        {
            AddRolePolicy(options, ModerationPolicies.Reporter, "ModerationReporter");
            AddRolePolicy(options, ModerationPolicies.Triage, "ModerationTriage");
            AddRolePolicy(options, ModerationPolicies.Investigator, "ModerationInvestigator");
            AddRolePolicy(options, ModerationPolicies.Decision, "ModerationDecision");
            AddRolePolicy(options, ModerationPolicies.Admin, "ModerationAdmin", "SysAdmin");
            options.AddPolicy(ModerationPolicies.DecisionExport, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("client_id");
                policy.RequireAssertion(context => context.User.Claims
                    .Where(claim => claim.Type == "scope")
                    .SelectMany(claim => claim.Value.Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .Contains(exportScope, StringComparer.Ordinal));
            });
        });
        if (configuration is not null
            && RuntimeIdentity.Resolve(configuration).ExposesBusinessRoutes())
        {
            services.AddOptions<ModerationDecisionExportOptions>()
                .Bind(configuration.GetSection(ModerationDecisionExportOptions.SectionName))
                .Validate(
                    ModerationDecisionExportOptions.IsValid,
                    "DecisionExport requires a base64 cursor key of at least 32 bytes and a bounded lifetime.")
                .ValidateOnStart();
            services.AddSingleton<ModerationDecisionExportCursor>();
            services.AddScoped<ExportModerationDecisionStateQuery>();
        }
        return services;
    }

    private static void AddRolePolicy(
        AuthorizationOptions options,
        string name,
        params string[] roles) =>
        options.AddPolicy(name, policy =>
            policy.RequireAuthenticatedUser().RequireClaim("sub").RequireRole(roles));
}
