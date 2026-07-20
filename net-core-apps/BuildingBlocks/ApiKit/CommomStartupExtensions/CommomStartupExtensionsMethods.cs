using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using AuthUtils;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.OpenApi;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using System.Globalization;
using System.Threading;
using Polly;

namespace CommomStartupExtensions
{
    public static class CommomStartupExtensionsMethods
    {
        public static IServiceCollection ConfigureCommomExtensions(this IServiceCollection services,
                                                                    IConfiguration? configuration,
                                                                    Cors corsSettings,
                                                                    string apiName,
                                                                    string openApiDocumentName,
                                                                    string[] authPolicies,
                                                                    string authScopeSection,
                                                                    bool configureAuthentication = true)
        {
            ArgumentNullException.ThrowIfNull(corsSettings);
            ArgumentException.ThrowIfNullOrWhiteSpace(corsSettings.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(authScopeSection);

            var allowedOrigins = NormalizeCorsOrigins(corsSettings.Urls);

            services.ConfigureControllers();
            if (configureAuthentication)
            {
                services.ConfigureAuthentication(authPolicies, configuration, authScopeSection);
            }
            else
            {
                // Worker and migration hosts expose only anonymous operational endpoints.
                // Register the middleware primitives without binding an API identity secret.
                services.AddAuthentication();
                services.AddAuthorization();
            }

            services.AddHttpContextAccessor();

            services.AddCors(options =>
            {
                options.AddPolicy(
                    name: corsSettings.Name,
                    builder =>
                    {
                        // With no configured origins the policy intentionally grants no
                        // cross-origin access. Origin matching remains exact; wildcard and
                        // private-network heuristics are not safe production policies.
                        if (allowedOrigins.Length > 0)
                            builder.WithOrigins(allowedOrigins);

                        builder.AllowAnyHeader()
                            .AllowAnyMethod();
                    });
            });

            // ASP.NET Core 10 native OpenAPI generator. Schemas are inferred from the
            // System.Text.Json metadata configured in ConfigureControllers() (string enums and
            // UTC ISO-8601 dates), so no schema/operation filters
            // are required to mirror the previous Swashbuckle behaviour.
            // A single canonical document is served at /openapi/{documentName}.json.
            services.AddOpenApi(openApiDocumentName, options =>
            {
                options.AddDocumentTransformer((document, context, ct) =>
                {
                    document.Info ??= new OpenApiInfo();
                    document.Info.Title = apiName;
                    document.Info.Version = "current";

                    document.Components ??= new OpenApiComponents();
                    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                    document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        BearerFormat = "JWT",
                        In = ParameterLocation.Header,
                        Name = "Authorization",
                        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                    };
                    return Task.CompletedTask;
                });

                // Advertise Bearer security only on protected operations.
                options.AddOperationTransformer((operation, context, ct) =>
                {
                    var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                    var isAnonymous = metadata.OfType<IAllowAnonymous>().Any();
                    var isProtected = metadata.OfType<IAuthorizeData>().Any();
                    if (isAnonymous || !isProtected)
                        return Task.CompletedTask;

                    operation.Security ??= new List<OpenApiSecurityRequirement>();
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = new List<string>(),
                    });
                    return Task.CompletedTask;
                });
            });

            return services;
        }

        public static IServiceCollection ConfigureControllers(
            this IServiceCollection services)
        {
            services.AddControllers()
                .AddJsonOptions(options =>
                    ApiJsonSerialization.Configure(options.JsonSerializerOptions));

            // Controllers and Minimal APIs share the exact same strict System.Text.Json contract.
            services.ConfigureHttpJsonOptions(options =>
                ApiJsonSerialization.Configure(options.SerializerOptions));

            return services;
        }


        public static IServiceCollection ConfigureAuthentication(
            this IServiceCollection services,
            string[] authPolicies,
            IConfiguration? configuration = null)
            => services.ConfigureAuthentication(
                authPolicies,
                configuration,
                authScopeSection: "Identity");

        public static IServiceCollection ConfigureAuthentication(
            this IServiceCollection services,
            string[] authPolicies,
            IConfiguration? configuration,
            string authScopeSection)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(authScopeSection);

            services.AddAuthentication(options =>
            {
                options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            });

            services.AddOpenIddict()
                .AddValidation(options =>
                {
                    var issuer = GetSetting("Identity:ServiceUrl", configuration);
                    var clientId = GetSetting("Identity:ClientId", configuration);
                    var validationMode = configuration?["Identity:ValidationMode"]?.Trim();
                    if (string.IsNullOrWhiteSpace(validationMode))
                        validationMode = "Introspection";

                    options.SetIssuer(issuer);
                    var audience = configuration?["Identity:Audience"];
                    if (string.IsNullOrWhiteSpace(audience))
                        audience = clientId;
                    options.AddAudiences(audience);

                    if (validationMode.Equals("Introspection", StringComparison.OrdinalIgnoreCase))
                    {
                        options.UseIntrospection()
                            .SetClientId(clientId)
                            .SetClientSecret(GetSetting("Identity:ClientSecret", configuration));
                    }
                    else if (validationMode.Equals("Discovery", StringComparison.OrdinalIgnoreCase))
                    {
                        // Discovery validates self-contained access tokens locally after
                        // retrieving the issuer's asymmetric signing keys. An explicit
                        // audience prevents a token minted for another API being accepted.
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "Identity:ValidationMode must be either 'Introspection' or 'Discovery'.");
                    }

                    var totalTimeout = GetBoundedMillisecondsSetting(
                        configuration,
                        "Identity:Transport:TotalTimeoutMilliseconds",
                        defaultValue: 15_000,
                        minimum: 100,
                        maximum: 60_000);
                    var attemptTimeout = GetBoundedMillisecondsSetting(
                        configuration,
                        "Identity:Transport:AttemptTimeoutMilliseconds",
                        defaultValue: 5_000,
                        minimum: 50,
                        maximum: 30_000);
                    var retryDelay = GetBoundedMillisecondsSetting(
                        configuration,
                        "Identity:Transport:RetryDelayMilliseconds",
                        defaultValue: 200,
                        minimum: 0,
                        maximum: 5_000);
                    if (attemptTimeout >= totalTimeout)
                    {
                        throw new InvalidOperationException(
                            "Identity transport attempt timeout must be shorter than its total timeout.");
                    }

                    var transport = options.UseSystemNetHttp();
                    transport.ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);
                    transport.SetHttpResiliencePipeline(builder => builder
                        .AddConcurrencyLimiter(permitLimit: 128, queueLimit: 0)
                        .AddTimeout(TimeSpan.FromMilliseconds(totalTimeout))
                        .AddRetry(new HttpRetryStrategyOptions
                        {
                            MaxRetryAttempts = 2,
                            Delay = TimeSpan.FromMilliseconds(retryDelay),
                            UseJitter = true,
                        })
                        .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                        {
                            FailureRatio = 0.5,
                            MinimumThroughput = 20,
                            SamplingDuration = TimeSpan.FromSeconds(30),
                            BreakDuration = TimeSpan.FromSeconds(15),
                        })
                        .AddTimeout(TimeSpan.FromMilliseconds(attemptTimeout)));

                    options.UseAspNetCore();
                });

            services.AddPolicy(authPolicies, configuration, authScopeSection);

            return services;
        }

        private static IServiceCollection AddPolicy(
            this IServiceCollection services,
            string[]? policies = null,
            IConfiguration? configuration = null,
            string authScopeSection = "Identity")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(authScopeSection);

            var selectedPolicies = policies?.ToHashSet(StringComparer.Ordinal)
                ?? new HashSet<string>(StringComparer.Ordinal);
            var writeScopeKey = $"{authScopeSection}:WriteScope";
            var readScopeKey = $"{authScopeSection}:ReadScope";
            var apiWriteScope = GetSetting(writeScopeKey, configuration);
            var apiReadScope = GetSetting(readScopeKey, configuration);

            EnsureScopeConfigured(
                selectedPolicies.Contains(AuthPolicies.ClientWriteAccess)
                || selectedPolicies.Contains(AuthPolicies.ClientFullAccess),
                writeScopeKey,
                apiWriteScope);
            EnsureScopeConfigured(
                selectedPolicies.Contains(AuthPolicies.ClientReadAccess)
                || selectedPolicies.Contains(AuthPolicies.ClientFullAccess),
                readScopeKey,
                apiReadScope);

            var rolePolicies = new Dictionary<string, string[]>
            {
                {
                    AuthPolicies.UserAdminAccess,
                    ["SysAdmin"]
                },
            };
            var scopePolicies = new Dictionary<string, string[][]>
            {
                {
                    AuthPolicies.ClientWriteAccess,
                    [
                        [OpenIddictConstants.Claims.Scope, apiWriteScope]
                    ]
                },
                {
                    AuthPolicies.ClientReadAccess,
                    [
                        [OpenIddictConstants.Claims.Scope, apiReadScope]
                    ]
                },
                {
                    AuthPolicies.ClientFullAccess,
                    [
                        [OpenIddictConstants.Claims.Scope, apiWriteScope],
                        [OpenIddictConstants.Claims.Scope, apiReadScope]
                    ]
                }
            };
            var clientIdPolicies = new Dictionary<string, string[][]>
            {
                {
                    AuthPolicies.FromReactApp,
                    [
                        [OpenIddictConstants.Claims.ClientId, "react"]
                    ]
                },
            };


            // Bare [Authorize] and RequireAuthorization() endpoints still need the
            // default authorization services even when no named policies were inferred.
            services.AddAuthorization(options =>
            {
                foreach (var scope in scopePolicies.Where(e => policies?.Contains(e.Key) == true))
                {
                    options.AddPolicy(scope.Key, policy =>
                    {
                        policy.RequireAuthenticatedUser();

                        foreach (var key in scope.Value)
                        {
                            policy.RequireClaim(key[0], key[1]);
                        }
                    });
                }

                foreach (var scope in clientIdPolicies.Where(e => policies?.Contains(e.Key) == true))
                {
                    options.AddPolicy(scope.Key, policy =>
                    {
                        policy.RequireAuthenticatedUser();

                        foreach (var key in scope.Value)
                        {
                            policy.RequireClaim(key[0], key[1]);
                        }
                    });
                }

                foreach (var scope in rolePolicies.Where(e => policies?.Contains(e.Key) == true))
                {
                    options.AddPolicy(scope.Key, policy =>
                    {
                        policy.RequireAuthenticatedUser();

                        foreach (var key in scope.Value)
                        {
                            policy.RequireRole(key);
                        }
                    });
                }
            });

            return services;
        }

        private static void EnsureScopeConfigured(bool required, string key, string? value)
        {
            if (required && string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Authorization policy configuration requires a non-empty '{key}' value.");
            }
        }

        private static string GetSetting(string setting, IConfiguration? configuration) =>
            configuration?[setting] ?? string.Empty;

        private static int GetBoundedMillisecondsSetting(
            IConfiguration? configuration,
            string key,
            int defaultValue,
            int minimum,
            int maximum)
        {
            var configured = configuration?[key];
            if (string.IsNullOrWhiteSpace(configured))
                return defaultValue;
            if (!int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                || value < minimum
                || value > maximum)
            {
                throw new InvalidOperationException(
                    $"{key} must be an integer between {minimum} and {maximum} milliseconds.");
            }

            return value;
        }

        private static string[] NormalizeCorsOrigins(IEnumerable<string>? configuredOrigins)
        {
            const int maxOrigins = 64;
            const int maxOriginLength = 512;

            var origins = (configuredOrigins ?? Array.Empty<string>())
                .Where(origin => !string.IsNullOrWhiteSpace(origin))
                .ToArray();

            if (origins.Length > maxOrigins)
            {
                throw new InvalidOperationException(
                    $"CORS configuration contains {origins.Length} origins; at most {maxOrigins} are allowed.");
            }

            var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var configuredOrigin in origins)
            {
                var origin = configuredOrigin.Trim();
                if (origin.Length > maxOriginLength)
                    throw new InvalidOperationException("A configured CORS origin exceeds the maximum length.");

                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                    || !string.IsNullOrEmpty(uri.UserInfo)
                    || uri.AbsolutePath != "/"
                    || !string.IsNullOrEmpty(uri.Query)
                    || !string.IsNullOrEmpty(uri.Fragment))
                {
                    throw new InvalidOperationException(
                        $"CORS origin '{origin}' must be an absolute HTTP(S) origin without credentials, path, query, or fragment.");
                }

                normalized.Add(uri.GetLeftPart(UriPartial.Authority));
            }

            return normalized.ToArray();
        }
    }

    public class Cors
    {
        public string Name { get; set; }
        public string[] Urls { get; set; }
        public Cors(string name, string[] urls)
        {
            Name = name;
            Urls = urls;
        }
    }
}
