using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Newtonsoft.Json;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using System.Globalization;
using System.Reflection;

namespace CommomStartupExtensions
{
    public static class CommomStartupExtensionsMethods
    {
        // Back-compat overload matching the legacy net-core-apps shape.
        // Forwards to the canonical signature with derived defaults.
        public static IServiceCollection ConfigureCommomExtensions(this IServiceCollection services,
                                                                    IConfiguration configuration,
                                                                    Cors corsSettings,
                                                                    string[] authPolicies = null)
            => services.ConfigureCommomExtensions(corsSettings,
                                                  apiName: corsSettings?.Name ?? "API",
                                                  apiVersion: "v1",
                                                  authPolicies: authPolicies ?? Array.Empty<string>());

        public static IServiceCollection ConfigureCommomExtensions(this IServiceCollection services,
                                                                    Cors corsSettings,
                                                                    string apiName,
                                                                    string apiVersion,
                                                                    string[] authPolicies)
        {
            services
                .ConfigureControllers()
                .ConfigureAuthentication(authPolicies);

            services.AddHttpContextAccessor();

            services.AddCors(options =>
            {
                options.AddPolicy(
                    name: corsSettings.Name,
                    builder =>
                    {
                        builder
                            .WithOrigins(corsSettings.Urls)
                            // 2. Dynamically allow any local network IP
                            .SetIsOriginAllowed(origin =>
                            {
                                if (string.IsNullOrWhiteSpace(origin)) return false;

                                var host = new Uri(origin).Host;

                                // Return true if the host is localhost or falls into a private IP range
                                return host == "localhost" ||
                                    host.StartsWith("192.168.") ||
                                    host.StartsWith("10.") ||
                                    host.StartsWith("172.");
                            })
                            .AllowAnyOrigin()
                            .AllowAnyHeader()
                            .AllowAnyMethod();
                    });
            });

            // ASP.NET Core 10 native OpenAPI generator. Schemas are inferred from the
            // System.Text.Json metadata configured in ConfigureControllers() (string enums,
            // ReferenceHandler.IgnoreCycles, UTC ISO-8601 dates), so no schema/operation filters
            // are required to mirror the previous Swashbuckle behaviour.
            // Document name = apiVersion (e.g. "v1") -> served at /openapi/{apiVersion}.json.
            services.AddOpenApi(apiVersion, options =>
            {
                options.AddDocumentTransformer((document, context, ct) =>
                {
                    document.Info ??= new OpenApiInfo();
                    document.Info.Title = apiName;
                    document.Info.Version = apiVersion;

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

                // Advertise Bearer security on every operation so the Scalar UI shows the
                // "Authorize" affordance for protected endpoints.
                options.AddOperationTransformer((operation, context, ct) =>
                {
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
            services.AddControllers(options =>
            {
                options.ModelBinderProviders.Insert(0, new InvariantDecimalModelBinderProvider());
            })
            .AddNewtonsoftJson(options => {
                options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                options.SerializerSettings.Converters =
                        new List<JsonConverter>()
                        {
                            new Newtonsoft.Json.Converters.StringEnumConverter(),
                        };
                options.SerializerSettings.DateFormatString = "yyyy-MM-ddTHH:mm:ss.fffffffZ";
                options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
            });

            // Mirror the controllers' Newtonsoft.Json configuration on the System.Text.Json options
            // used by the retained Minimal APIs so request/response payloads behave identically across both
            // styles: string enums, cycle-tolerant serialisation, and UTC ISO-8601 (7 fractional
            // digits, trailing 'Z') for DateTime / DateTimeOffset.
            services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                options.SerializerOptions.Converters.Add(new UtcIso8601DateTimeConverter());
                options.SerializerOptions.Converters.Add(new UtcIso8601DateTimeOffsetConverter());
                options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
                options.SerializerOptions.Converters.Add(new EmptyStringDateTimeConverter());
                options.SerializerOptions.Converters.Add(new EmptyStringInt32Converter());
            });

            return services;
        }


        public static IServiceCollection ConfigureAuthentication(this IServiceCollection services, string[] authPolicies)
        {
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            });
    
            services.AddOpenIddict()
                .AddValidation(options =>
                {
                    options.SetIssuer(GetSetting("IDENTITY_SERVICE_URL"));

                    options.UseIntrospection()
                          .SetClientId(GetSetting("IDENTITY_SERVICE_CLIENT_ID"))
                          .SetClientSecret(GetSetting("IDENTITY_SERVICE_CLIENT_SECRET"));

                    options.UseSystemNetHttp();

                    options.UseAspNetCore();
                });

            services.AddPolicy(authPolicies);

            return services;
        }

        private static IServiceCollection AddPolicy(this IServiceCollection services, string[] policies = null)
        {
            var walletWriteScope = GetSetting("IDENTITY_SERVICE_CLIENT_WRITE_SCOPE");
            var walletReadScope = GetSetting("IDENTITY_SERVICE_CLIENT_READ_SCOPE");
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
                        [OpenIddictConstants.Claims.Scope, walletWriteScope]
                    ]
                },
                {
                    AuthPolicies.ClientReadAccess,
                    [ 
                        [OpenIddictConstants.Claims.Scope, walletReadScope]
                    ]
                },
                {
                    AuthPolicies.ClientFullAccess,
                    [
                        [OpenIddictConstants.Claims.Scope, walletWriteScope],
                        [OpenIddictConstants.Claims.Scope, walletReadScope]
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


            if (policies?.Count() > 0)
            {
                services.AddAuthorization(options =>
                {
                    foreach (var scope in scopePolicies?.Where(e => policies.Contains(e.Key)))
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

                    foreach (var scope in clientIdPolicies?.Where(e => policies.Contains(e.Key)))
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

                    foreach (var scope in rolePolicies?.Where(e => policies.Contains(e.Key)))
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
            }

            return services;
        }

        private static string GetSetting(string setting) => Environment.GetEnvironmentVariable(setting);
    }

    public static class AuthPolicies
    {
        public const string UserAdminAccess = "UserAdminAccess";
        public const string ClientWriteAccess = "ClientWriteAccess";
        public const string ClientReadAccess = "ClientReadAccess";
        public const string ClientFullAccess = "ClientFullAccess";
        public const string FromReactApp = "FromReactApp";
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
