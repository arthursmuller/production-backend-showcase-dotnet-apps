using Microsoft.AspNetCore.Authorization;
using Interaction.Application.Features.Exports;
using Interaction.Application.Options;
using InspacyApiHost;

namespace Interaction.Api;

public static class InteractionPolicies
{
    public const string AuthoritativeExport = "InteractionAuthoritativeExport";
}

public static class InteractionAuthorization
{
    public const string DefaultExportScope = "interaction.authority.export";

    public static IServiceCollection AddInteractionAuthorization(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var requiredScope = configuration["Identity:AuthoritativeExportScope"]?.Trim();
        if (string.IsNullOrWhiteSpace(requiredScope))
            requiredScope = DefaultExportScope;

        services.AddAuthorization(options =>
            options.AddPolicy(InteractionPolicies.AuthoritativeExport, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("client_id");
                policy.RequireAssertion(context => context.User.Claims
                    .Where(claim => claim.Type == "scope")
                    .SelectMany(claim => claim.Value.Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .Contains(requiredScope, StringComparer.Ordinal));
            }));
        if (RuntimeIdentity.Resolve(configuration).ExposesBusinessRoutes())
        {
            services.AddOptions<AuthoritativeExportOptions>()
                .Bind(configuration.GetSection(AuthoritativeExportOptions.SectionName))
                .Validate(
                    AuthoritativeExportOptions.IsValid,
                    "AuthoritativeExport requires a base64 cursor key of at least 32 bytes and a bounded lifetime.")
                .ValidateOnStart();
            services.AddSingleton<InteractionAuthoritativeExportCursor>();
        }
        return services;
    }
}
