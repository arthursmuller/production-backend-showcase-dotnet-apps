using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ApiExceptionHandling;
using AuthUtils;
using CommomStartupExtensions;
using Cqrs.Extensions;
using EfCoreMigrations;
using MessageBrokerCore.Abstractions;
using MessageBrokerExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MySqlMigrations;
using Scalar.AspNetCore;
using SecretProvider;

namespace InspacyApiHost
{
    /// <summary>
    /// Configures CQRS handler registration for the host. When omitted, the host discovers
    /// whether registration is needed from the entry, application and infrastructure assemblies.
    /// Pass an instance to configure explicit behavior and any <see cref="AdditionalAssemblies"/>.
    /// </summary>
    public sealed class CqrsOptions
    {
        /// <summary>
        /// Enables CQRS registration. Set to <c>false</c> to explicitly opt out when
        /// <see cref="ApiOptions.WithCqrs"/> would otherwise be auto-detected.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Extra assemblies scanned by <c>RegisterCQRS</c> in addition to the
        /// entry assembly and the auto-detected application and infrastructure assemblies.
        /// </summary>
        public Assembly[] AdditionalAssemblies { get; set; } = Array.Empty<Assembly>();
    }

    /// <summary>
    /// Configures startup-time database schema/migration application.
    /// <para>
    /// When attached to <see cref="ApiOptions.WithDatabases"/>, the host runs
    /// (after <c>Build()</c> and before serving traffic):
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="EfCoreMigrationsExtensions.ApplySchemaForAllAsync"/>
    ///         for every entry in <see cref="Contexts"/> (auto-discovered when null/empty).</item>
    ///   <item><see cref="DatabaseMigrationExtensions.ApplyMySqlMigrationsForAsync"/> for every
    ///         entry in <see cref="MigrationConfigurations"/> (auto-discovered when null/empty).</item>
    /// </list>
    /// Auto-discovery scans the entry assembly + the infra assembly resolved via
    /// <see cref="ApiOptions.WithInfra"/>.
    /// </summary>
    public sealed class WithDatabasesOptions
    {
        /// <summary>
        /// Explicit list of <see cref="Microsoft.EntityFrameworkCore.DbContext"/> derivatives whose
        /// schema must be applied at startup. Leave null/empty to auto-discover.
        /// </summary>
        public Type[]? Contexts { get; set; }

        /// <summary>
        /// Explicit list of <see cref="IMigrationsConfiguration"/> implementations whose raw-SQL
        /// migrations must be applied at startup. Leave null/empty to auto-discover.
        /// </summary>
        public Type[]? MigrationConfigurations { get; set; }

        /// <summary>
        /// When <c>true</c>, the dedicated Migration role applies schemas/migrations and exits.
        /// Omitted values are enabled only for that role. Request and worker roles never migrate.
        /// </summary>
        public bool? Enabled { get; set; }
    }


    /// <summary>
    /// Options describing an Inspacy API host (cors policy, app name, auth policies).
    /// </summary>
    public sealed class ApiOptions
    {
        public required string AppName { get; set; }
        public string CorsPolicyName
        {
            get => string.IsNullOrEmpty(field) ? $"{AppName}Service" : field;
            set => field = value;
        }
        public string OpenApiDocumentName { get; set; } = "api";
        public string[]? AllowedCorsDomains { get; set; }
        /// <summary>
        /// Named authorization policies used by this API. When omitted, policies are inferred
        /// from controller authorization metadata and named policies referenced by
        /// <c>Map*Routes</c> route mappers. Set an empty array to disable inference.
        /// </summary>
        public string[]? AuthPolicies { get; set; }

        /// <summary>
        /// Selects the process role. Configure <c>Runtime:Role</c> in each deployment;
        /// production defaults to the safe request-only <see cref="ApiRuntimeRole.Api"/> role.
        /// <see cref="ApiRuntimeRole.Combined"/> is intended for local development only.
        /// </summary>
        public ApiRuntimeRole? RuntimeRole { get; set; }

        /// <summary>
        /// Optional. Used by reflection-based discovery as the starting assembly
        /// for routes / jobs / infra scanning. Defaults to <see cref="Assembly.GetEntryAssembly"/>.
        /// </summary>
        public Assembly? EntryAssembly { get; set; }

        /// <summary>
        /// When <c>true</c>, scans the entry assembly's referenced assemblies for a static
        /// <c>IServiceCollection ConfigureInfraestructure(this IServiceCollection, IConfiguration)</c>
        /// extension method (typically defined in the matching <c>*.Infra*</c> project) and invokes it.
        /// When omitted, the host enables this feature only when that extension is found.
        /// Set to <c>false</c> to explicitly opt out.
        /// </summary>
        public bool? WithInfra { get; set; }

        /// <summary>
        /// When <c>true</c>, scans the entry assembly's referenced assemblies for a static
        /// <c>IServiceCollection ConfigureApplication(this IServiceCollection, IConfiguration)</c>
        /// extension method (typically defined in the matching <c>*.Application</c> project) and invokes it.
        /// The discovered assembly is also included in automatic CQRS registration.
        /// When omitted, the host enables this feature only when that extension is found.
        /// Set to <c>false</c> to explicitly opt out.
        /// </summary>
        public bool? WithApplication { get; set; }

        /// <summary>
        /// When enabled, registers CQRS handlers via <see cref="CqrsExtensions.RegisterCQRS"/>
        /// for the entry assembly + the auto-detected application and infrastructure assemblies + any
        /// <see cref="CqrsOptions.AdditionalAssemblies"/>.
        /// When omitted, CQRS is enabled only when a concrete <c>ICommandQuery</c> is found.
        /// Use <c>new CqrsOptions { Enabled = false }</c> to explicitly opt out.
        /// </summary>
        public CqrsOptions? WithCqrs { get; set; }

        /// <summary>
        /// When <c>true</c>, scans the entry assembly for every static extension method whose
        /// name matches <c>Map*Routes</c> on <see cref="IEndpointRouteBuilder"/> and invokes
        /// each one against the built <see cref="WebApplication"/>.
        /// May be combined with <see cref="WithControllers"/> so an API can mix attribute-routed
        /// controllers (e.g. for <c>[FromForm]</c> endpoints) with minimal-API route groups.
        /// When omitted, the host enables this feature only when a matching mapper is found.
        /// Set to <c>false</c> to explicitly opt out.
        /// </summary>
        public bool? WithRouteGroups { get; set; }

        /// <summary>
        /// When <c>true</c>, the host calls <c>app.MapControllers()</c> at build time so attribute-routed
        /// MVC controllers in the entry assembly are wired up. May be combined with
        /// <see cref="WithRouteGroups"/> so an API can expose both controllers and minimal-API
        /// endpoints from the same host. When omitted, the host enables this feature only when
        /// an MVC controller is found. Set to <c>false</c> to explicitly opt out.
        /// </summary>
        public bool? WithControllers { get; set; }

        /// <summary>
        /// When <c>true</c>, selects the configured message-broker provider from
        /// <c>Broker:Provider</c>, configures its connection and discovers both API-owned
        /// queue consumers and transport publishers. Environment overrides use the canonical
        /// .NET hierarchy (for example <c>Broker__Provider</c> and
        /// <c>Broker__RabbitMq__Host</c>) so validation and typed option binding observe the
        /// same effective values. When omitted, this feature is enabled only when
        /// <c>Broker:Provider</c> is configured.
        /// Set to <c>false</c> to explicitly opt out.
        /// </summary>
        public bool? WithBroker { get; set; }

        /// <summary>
        /// Independently gates receive endpoints. Request replicas can publish through the
        /// broker/outbox while leaving consumers to separately scaled worker replicas.
        /// </summary>
        public bool? WithConsumers { get; set; }

        /// <summary>
        /// When <c>true</c>, scans the entry assembly for every concrete <see cref="IHostedService"/>
        /// implementation and registers it directly. When omitted, the host enables this feature
        /// only when a concrete <see cref="BackgroundService"/> is found. Set to <c>false</c> to
        /// explicitly opt out.
        /// </summary>
        public bool? WithJobs { get; set; }

        /// <summary>
        /// Configures startup-time database schema/migration application.
        /// Uses full auto-discovery in the dedicated Migration role. Request and worker
        /// roles default this to disabled and reject attempts to apply DDL.
        /// </summary>
        public WithDatabasesOptions WithDatabases { get; init; } = new();


        /// <summary>
        /// Optional callback executed at the end of <see cref="InspacyApiHostExtensions.Api"/>
        /// (after the standard service registration and after <see cref="WithInfra"/> /
        /// <see cref="WithCqrs"/> / <see cref="WithJobs"/>). Use this to add
        /// service-collection registrations not covered by the standard host
        /// (e.g. <c>builder.Services.AddMediator(...)</c>).
        /// </summary>
        public Action<WebApplicationBuilder>? ConfigureServices { get; set; }

        /// <summary>
        /// Optional callback executed inside <see cref="InspacyApiBuilder.Build"/>
        /// after <c>UseInspacyApi</c> (CORS / AuthN / AuthZ / swagger / healthcheck /
        /// MapControllers) and after the canonical <c>UseExceptionHandler</c> middleware is wired,
        /// but BEFORE route discovery, database migrations and queue consumer subscription.
        /// Use this to register additional middleware in the request pipeline.
        /// </summary>
        public Action<WebApplication>? ConfigurePipeline { get; set; }

    }

    /// <summary>
    /// Returned by <see cref="InspacyApiHostExtensions.Api"/> to allow chaining <see cref="RunApp"/>.
    /// </summary>
    public sealed class InspacyApiBuilder
    {
        private readonly ApiOptions _options;
        private readonly Assembly _entryAssembly;
        private readonly Assembly? _infraAssembly;

        /// <summary>The underlying <see cref="WebApplicationBuilder"/>.</summary>
        public WebApplicationBuilder Builder { get; }

        internal InspacyApiBuilder(
            WebApplicationBuilder builder,
            ApiOptions options,
            Assembly entryAssembly,
            Assembly? infraAssembly)
        {
            Builder = builder;
            _options = options;
            _entryAssembly = entryAssembly;
            _infraAssembly = infraAssembly;
        }

        /// <summary>
        /// Builds the <see cref="WebApplication"/>, wires the standard Inspacy middleware pipeline,
        /// maps controllers when <see cref="ApiOptions.WithControllers"/> is enabled (or otherwise
        /// auto-discovers <c>Map*Routes</c> endpoints when <see cref="ApiOptions.WithRouteGroups"/>
        /// is <c>true</c>), and subscribes message-broker consumers when
        /// <see cref="ApiOptions.WithBroker"/> is enabled. This build-only method does not
        /// apply database schemas; callers with
        /// <see cref="ApiOptions.WithDatabases"/> enabled must use <see cref="BuildAsync"/>.
        /// </summary>
        /// <returns>The fully configured <see cref="WebApplication"/>; callers may add
        /// further middleware before invoking the cancellation-aware asynchronous host
        /// runner.</returns>
        public WebApplication Build()
        {
            if (_options.WithDatabases is { Enabled: true })
            {
                throw new InvalidOperationException(
                    "Database migrations require the cancellation-aware BuildAsync API.");
            }

            return BuildApplication();
        }

        /// <summary>
        /// Builds the application and applies all configured database migrations before the
        /// host or any consumer/job starts. Cancellation is propagated through migration I/O
        /// and retry delays.
        /// </summary>
        public async Task<WebApplication> BuildAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var app = BuildApplication();
            try
            {
                if (_options.WithDatabases is { Enabled: true })
                {
                    await InspacyApiHostExtensions.ApplyDatabasesAsync(
                        app,
                        _options.WithDatabases,
                        _entryAssembly,
                        _infraAssembly,
                        cancellationToken);
                }

                return app;
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }
        }

        private WebApplication BuildApplication()
        {
            var app = Builder.Build();
            app.UseForwardedHeaders();
            app.UseMiddleware<RequestLoggingMiddleware>();

            app.UseExceptionHandler();

            app.UseInspacySecurityStatusCodePages();

            app.UseRequestTimeouts();
            app.UseInspacyApi(
                _options.CorsPolicyName,
                _options.RuntimeRole ?? ApiRuntimeRole.Api);

            // User-supplied middleware extension point (UseRouting-time additions, custom auth,
            // request logging, etc.). Runs after exception handling so unhandled errors are caught.
            _options.ConfigurePipeline?.Invoke(app);

            // ---- Routing ----
            // Controllers and route groups are independent. Api() resolves omitted values from
            // the entry assembly before this builder is created, while explicit false values
            // remain opt-outs.
            if (_options.WithControllers == true)
                app.MapControllers();

            if (_options.WithRouteGroups == true)
                InspacyApiHostExtensions.MapDiscoveredRoutes(app, _entryAssembly);

            InspacyApiHostExtensions.ValidateMappedRoutesForRuntimeRole(app, _options.RuntimeRole);

            return app;
        }

        /// <summary>
        /// Builds and runs the host asynchronously. The dedicated Migration role applies the
        /// schema and exits without starting request, consumer, or job workloads.
        /// </summary>
        public async Task RunAppAsync(CancellationToken cancellationToken = default)
        {
            using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };
            EventHandler processExitHandler = (_, _) => shutdown.Cancel();

            Console.CancelKeyPress += cancelHandler;
            AppDomain.CurrentDomain.ProcessExit += processExitHandler;
            try
            {
                await using var app = await BuildAsync(shutdown.Token);
                if (_options.RuntimeRole == ApiRuntimeRole.Migration)
                    return;

                await app.RunWithTryCatchAsync(_options.AppName, shutdown.Token);
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
                AppDomain.CurrentDomain.ProcessExit -= processExitHandler;
            }
        }

    }

    /// <summary>
    /// One-shot entry point for an Inspacy API host.
    /// <para>
    /// Replaces manual builder and host-runner setup with a cancellation-aware call to
    /// <c>await ApiBuilder.NewAsync(new ApiOptions { ... });</c>.
    /// </para>
    /// <para>
    /// Internally creates a <see cref="WebApplicationBuilder"/> using
    /// <see cref="Environment.GetCommandLineArgs"/> (skipping argv[0]), wires the standard
    /// Inspacy services / middleware via <see cref="InspacyApiHostExtensions.Api"/>, and then
    /// asynchronously runs until shutdown.
    /// </para>
    /// </summary>
    public static class ApiBuilder
    {
        /// <summary>
        /// Builds, configures, migrates, and runs an Inspacy API host asynchronously.
        /// </summary>
        public static Task NewAsync(
            ApiOptions options,
            CancellationToken cancellationToken = default) =>
            Create(options).RunAppAsync(cancellationToken);

        internal static InspacyApiBuilder Create(ApiOptions options, string[]? args = null)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            if (args == null)
            {
                // Skip the executable path (argv[0]) so behaviour matches
                // `WebApplication.CreateBuilder(args)`.
                var commandLineArgs = Environment.GetCommandLineArgs();
                args = commandLineArgs.Length > 1
                    ? commandLineArgs[1..]
                    : Array.Empty<string>();
            }

            var builder = WebApplication.CreateBuilder(args);
            var configuredRole = InspacyApiHostExtensions.ResolveRuntimeRole(builder.Configuration);
            if (options.RuntimeRole.HasValue && options.RuntimeRole.Value != configuredRole)
            {
                throw new InvalidOperationException(
                    $"ApiOptions.RuntimeRole '{options.RuntimeRole}' does not match Runtime:Role '{configuredRole}'. Runtime:Role is the authoritative workload identity boundary.");
            }
            options.RuntimeRole = configuredRole;
            InspacyApiHostExtensions.ValidateRuntimeRole(configuredRole, builder.Environment);
            builder.Configuration.AddSecretApiProvider(
                configuredRole.ToString(),
                builder.Environment.EnvironmentName);
            return builder.Api(options);
        }
    }

    /// <summary>
    /// Default CORS domains used by every Inspacy API.
    /// </summary>
    public static class InspacyDefaults
    {
        public static string[] DefaultCorsDomains(bool includeDevelopmentOrigins = false)
        {
            var defaults = new List<string>
            {
                "https://www.inspacy.com.br",
                "https://www.inspacy.com",
            };

            if (includeDevelopmentOrigins)
            {
                defaults.AddRange(
                [
                    "http://localhost",
                    "http://localhost:8081",
                    "http://localhost:8082",
                    "http://localhost:3001",
                    "http://localhost:3000",
                ]);
            }

            return defaults.ToArray();
        }
    }

    /// <summary>
    /// Centralised builder/middleware extensions for the top-level Program.cs pattern
    /// used by the retained APIs.
    /// </summary>
    public static class InspacyApiHostExtensions
    {
        /// <summary>
        /// Configures Serilog logger and registers the standard Inspacy service set
        /// (CORS, controllers, swagger, OpenIddict validation, auth policies).
        /// Also handles <see cref="ApiOptions.WithInfra"/> and <see cref="ApiOptions.WithCqrs"/>
        /// when set.
        /// </summary>
        public static InspacyApiBuilder Api(this WebApplicationBuilder builder, ApiOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.AppName))
                throw new ArgumentException("AppName is required", nameof(options));
            if (string.IsNullOrWhiteSpace(options.CorsPolicyName))
                throw new ArgumentException("CorsPolicyName is required", nameof(options));

            var entryAssembly = options.EntryAssembly ?? Assembly.GetEntryAssembly()
                ?? throw new InvalidOperationException(
                    "The API entry assembly could not be determined. Set ApiOptions.EntryAssembly explicitly.");

            options.RuntimeRole ??= ResolveRuntimeRole(builder.Configuration);
            ValidateRuntimeRole(options.RuntimeRole.Value, builder.Environment);

            var exposesBusinessRoutes = options.RuntimeRole is ApiRuntimeRole.Api
                or ApiRuntimeRole.Combined;

            EnsureReferencedAssembliesLoaded(entryAssembly);

            // Discover every optional feature before registering services. Nullable feature flags
            // preserve the distinction between an omitted option (auto) and an explicit false
            // (opt out), while explicit true still produces a useful error when its convention
            // cannot be satisfied.
            var routeMethods = DiscoverRouteMethods(entryAssembly).ToArray();
            var controllerTypes = DiscoverControllerTypes(entryAssembly).ToArray();

            var (applicationMethod, detectedApplicationAssembly) = FindStaticExtension(
                GetConventionAssemblies(entryAssembly, options.AppName, "Application"),
                "ConfigureApplication",
                typeof(IServiceCollection),
                typeof(IConfiguration));

            var infrastructureCandidates = GetConventionAssemblies(
                    entryAssembly,
                    options.AppName,
                    "Infraestructure")
                .Concat(GetConventionAssemblies(
                    entryAssembly,
                    options.AppName,
                    "Infrastructure"))
                .Distinct();

            var (infrastructureMethod, detectedInfrastructureAssembly) = FindStaticExtension(
                infrastructureCandidates,
                new[] { "ConfigureInfraestructure", "ConfigureInfrastructure" },
                typeof(IServiceCollection),
                typeof(IConfiguration));

            var boundaryAssemblies = new[]
                {
                    entryAssembly,
                    detectedApplicationAssembly,
                    detectedInfrastructureAssembly,
                }
                .OfType<Assembly>()
                .Distinct()
                .ToArray();

            var hostedServiceTypes = boundaryAssemblies
                .SelectMany(DiscoverHostedServiceTypes)
                .Distinct()
                .ToArray();

            options.WithRouteGroups ??= exposesBusinessRoutes && routeMethods.Length > 0;
            options.WithControllers ??= exposesBusinessRoutes && controllerTypes.Length > 0;
            options.WithApplication ??= applicationMethod != null;
            options.WithInfra ??= infrastructureMethod != null;
            options.WithBroker ??= options.RuntimeRole == ApiRuntimeRole.Migration
                ? false
                : HasConfiguredBrokerProvider(builder.Configuration);
            options.WithConsumers ??= options.RuntimeRole is ApiRuntimeRole.ConsumerWorker
                or ApiRuntimeRole.Combined;
            options.WithJobs ??= (options.RuntimeRole is ApiRuntimeRole.JobWorker
                or ApiRuntimeRole.Combined)
                && hostedServiceTypes.Length > 0;
            options.WithDatabases.Enabled ??= options.RuntimeRole == ApiRuntimeRole.Migration;

            ValidateRuntimeRoleOptions(options);

            if (options.WithCqrs == null && ContainsCqrsTypes(boundaryAssemblies))
                options.WithCqrs = new CqrsOptions();

            if (options.AuthPolicies == null)
            {
                options.AuthPolicies = DiscoverAuthPolicies(routeMethods, controllerTypes);
            }

            var configuredCorsDomains = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .GetChildren()
                .Select(origin => origin.Value)
                .OfType<string>()
                .Where(origin => !string.IsNullOrWhiteSpace(origin))
                .ToArray();
            var corsDomains = options.AllowedCorsDomains
                ?? (configuredCorsDomains.Length > 0
                    ? configuredCorsDomains
                    : InspacyDefaults.DefaultCorsDomains(builder.Environment.IsDevelopment()));
            if (!builder.Environment.IsDevelopment())
                ValidateProductionCorsDomains(corsDomains);

            builder.ConfigureLogger(options.AppName);

            builder.Services.AddInspacyProblemDetails();
            builder.Services.ConfigureObservability(
                builder.Configuration,
                $"{options.AppName}.API");
            var trafficSettings = builder.Services.ConfigureTrafficProtection(builder.Configuration);
            builder.WebHost.ConfigureKestrel(serverOptions =>
                serverOptions.Limits.MaxRequestBodySize = trafficSettings.MaxRequestBodyBytes);
            builder.Services.AddHealthChecks();
            builder.Services.AddPlatformTimeProvider();
            builder.Services.TryAddSingleton<RuntimeDrainState>();
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, RuntimeDrainLifecycleService>());
            builder.Services.AddHealthChecks().Add(new HealthCheckRegistration(
                "runtime-drain",
                serviceProvider => serviceProvider.GetRequiredService<RuntimeDrainState>(),
                HealthStatus.Unhealthy,
                tags: ["ready"]));

            builder.Services.ConfigureCommomExtensions(
                builder.Configuration,
                new Cors(options.CorsPolicyName, corsDomains),
                $"{options.AppName}.API",
                options.OpenApiDocumentName ?? "api",
                options.AuthPolicies,
                authScopeSection: options.AppName,
                configureAuthentication: exposesBusinessRoutes);

            // ---- WithApplication : reflect ConfigureApplication(IServiceCollection, IConfiguration) ----
            Assembly? applicationAssembly = null;
            if (options.WithApplication == true)
            {
                if (applicationMethod == null)
                    throw new InvalidOperationException(
                        "WithApplication=true but no static `IServiceCollection ConfigureApplication(this IServiceCollection, IConfiguration)` extension was found in any referenced assembly.");

                applicationAssembly = detectedApplicationAssembly
                    ?? throw new InvalidOperationException(
                        "The application extension was discovered without an owning assembly.");
                applicationMethod.Invoke(null, new object[] { builder.Services, builder.Configuration });
            }

            // ---- WithInfra : reflect ConfigureInfraestructure(IServiceCollection, IConfiguration) ----
            Assembly? infraAssembly = null;
            if (options.WithInfra == true)
            {
                if (infrastructureMethod == null)
                    throw new InvalidOperationException(
                        "WithInfra=true but no static `IServiceCollection ConfigureInfraestructure/ConfigureInfrastructure(this IServiceCollection, IConfiguration)` extension was found in any referenced assembly.");

                infraAssembly = detectedInfrastructureAssembly
                    ?? throw new InvalidOperationException(
                        "The infrastructure extension was discovered without an owning assembly.");
                infrastructureMethod.Invoke(null, new object[] { builder.Services, builder.Configuration });
            }

            ReadinessHealthChecks.AddDatabaseChecks(
                builder.Services,
                new[] { entryAssembly, applicationAssembly, infraAssembly }
                    .OfType<Assembly>());

            // ---- WithCqrs : RegisterCQRS for entry + application + infra (+ AdditionalAssemblies) ----
            if (options.WithCqrs is { Enabled: true })
            {
                var cqrsAssemblies = new[]
                    {
                        entryAssembly,
                        detectedApplicationAssembly,
                        detectedInfrastructureAssembly,
                    }
                    .Concat(options.WithCqrs.AdditionalAssemblies ?? Array.Empty<Assembly>())
                    .OfType<Assembly>()
                    .Distinct();

                foreach (var assembly in cqrsAssemblies)
                    builder.Services.RegisterCQRS(assembly);
            }

            // ---- WithJobs : concrete IHostedService discovery ----
            if (options.WithJobs == true)
            {
                foreach (var hostedType in hostedServiceTypes)
                    builder.Services.AddSingleton(typeof(IHostedService), hostedType);
            }

            // ---- User extension point: arbitrary additional service registrations ----
            options.ConfigureServices?.Invoke(builder);

            // A migration process must be able to validate the complete infrastructure graph
            // without acquiring transport capabilities. Provider adapters may still depend on
            // the transport-neutral publisher abstraction, so replace any registration with a
            // guard that always fails if migration code accidentally attempts publication.
            if (options.RuntimeRole == ApiRuntimeRole.Migration)
            {
                builder.Services.RemoveAll<IIntegrationMessagePublisher>();
                builder.Services.AddScoped<IIntegrationMessagePublisher, MigrationRoleMessagePublisher>();
            }

            // Register the bus after application/infrastructure/provider registrations so the
            // complete service-owned DbContext set can receive a local EF bus outbox. Consumers
            // remain independently gated by the runtime role.
            if (options.WithBroker == true)
            {
                var outboxContexts = EfCoreMigrationsExtensions.DiscoverContexts(
                    new[] { entryAssembly, applicationAssembly, infraAssembly }
                        .OfType<Assembly>())
                    .ToArray();

                ConfigureBroker(
                    builder.Services,
                    builder.Configuration,
                    entryAssembly,
                    options.WithConsumers == true,
                    outboxContexts);
            }

            return new InspacyApiBuilder(builder, options, entryAssembly, infraAssembly);
        }

        /// <summary>
        /// Selects and configures the MassTransit framework and RabbitMQ physical transport.
        /// Receive endpoints are registered only for consumer-capable runtime roles.
        /// </summary>
        private static void ConfigureBroker(
            IServiceCollection services,
            IConfiguration configuration,
            Assembly consumerAssembly,
            bool registerConsumers,
            IReadOnlyCollection<Type> outboxDbContextTypes)
        {
            var provider = configuration["Broker:Provider"];
            var transport = configuration["Broker:Transport"];

            if (string.IsNullOrWhiteSpace(provider))
                throw new InvalidOperationException(
                    "WithBroker=true requires a message-broker provider. " +
                    "Set 'Broker:Provider' to 'MassTransit'.");

            if (!string.Equals(provider.Trim(), "MassTransit", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Unsupported message-broker provider '{provider}'. " +
                    "The supported provider is 'MassTransit'.");

            if (!string.Equals(transport?.Trim(), "RabbitMq", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Unsupported message-broker transport '{transport ?? "<missing>"}'. " +
                    "Set 'Broker:Transport' to 'RabbitMq'.");

            services.AddInspacyMassTransit(
                configuration,
                consumerAssembly,
                registerConsumers,
                outboxDbContextTypes);
        }

        /// <summary>
        /// Returns true only for the canonical broker setting (including
        /// <c>Broker__Provider</c> environment overrides handled by IConfiguration).
        /// </summary>
        private static bool HasConfiguredBrokerProvider(IConfiguration configuration) =>
            !string.IsNullOrWhiteSpace(configuration["Broker:Provider"]);

        internal static ApiRuntimeRole ResolveRuntimeRole(IConfiguration configuration)
            => RuntimeIdentity.Resolve(configuration);

        internal static void ValidateRuntimeRole(
            ApiRuntimeRole role,
            IHostEnvironment environment)
            => RuntimeIdentity.ValidateEnvironment(role, environment.EnvironmentName);

        private static void ValidateProductionCorsDomains(IEnumerable<string> configuredOrigins)
        {
            foreach (var configuredOrigin in configuredOrigins)
            {
                if (!Uri.TryCreate(configuredOrigin, UriKind.Absolute, out var origin)
                    || origin.Scheme != Uri.UriSchemeHttps
                    || origin.IsLoopback)
                {
                    throw new InvalidOperationException(
                        $"Production CORS origin '{configuredOrigin}' must be a non-loopback HTTPS origin.");
                }
            }
        }

        private static void ValidateRuntimeRoleOptions(ApiOptions options)
        {
            var role = options.RuntimeRole
                ?? throw new InvalidOperationException("A runtime role must be resolved before validation.");
            var exposesRoutes = role is ApiRuntimeRole.Api or ApiRuntimeRole.Combined;

            if (!exposesRoutes && (options.WithControllers == true || options.WithRouteGroups == true))
                throw new InvalidOperationException($"Runtime role '{role}' cannot expose business routes.");
            if (options.WithConsumers == true && options.WithBroker != true)
                throw new InvalidOperationException("WithConsumers=true requires WithBroker=true.");
            if (options.WithConsumers == true
                && role is not (ApiRuntimeRole.ConsumerWorker or ApiRuntimeRole.Combined))
            {
                throw new InvalidOperationException(
                    $"Runtime role '{role}' cannot start receive endpoints.");
            }
            if (options.WithJobs == true
                && role is not (ApiRuntimeRole.JobWorker or ApiRuntimeRole.Combined))
            {
                throw new InvalidOperationException($"Runtime role '{role}' cannot start application jobs.");
            }
            if (options.WithDatabases.Enabled == true && role != ApiRuntimeRole.Migration)
            {
                throw new InvalidOperationException(
                    "Database migrations can run only in the dedicated Migration role.");
            }
            if (role == ApiRuntimeRole.Migration && options.WithDatabases.Enabled != true)
                throw new InvalidOperationException("The Migration role must enable database migration work.");
            if (role == ApiRuntimeRole.Migration && options.WithBroker == true)
            {
                throw new InvalidOperationException(
                    "The Migration role cannot connect to the message broker or publish messages.");
            }
        }

        /// <summary>
        /// Wires up the standard middleware pipeline used by Inspacy APIs:
        /// CORS, AuthN/AuthZ, OpenAPI document + Scalar API reference (Development) and the
        /// role-aware liveness/readiness endpoints.
        /// </summary>
        public static WebApplication UseInspacyApi(
            this WebApplication app,
            string corsPolicyName,
            ApiRuntimeRole runtimeRole = ApiRuntimeRole.Api)
        {
            if (string.IsNullOrWhiteSpace(corsPolicyName))
                throw new ArgumentException("corsPolicyName is required", nameof(corsPolicyName));

            app.UseCors(corsPolicyName)
               .UseAuthentication()
               .UseRateLimiter()
               .UseAuthorization();

            if (app.Environment.IsDevelopment()
                && runtimeRole is ApiRuntimeRole.Api or ApiRuntimeRole.Combined)
            {
                // One canonical document is served at /openapi/api.json.
                app.MapOpenApi();
                // Scalar UI is mounted at /scalar/api.
                app.MapScalarApiReference(options =>
                {
                    options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
                });
            }

            app.MapHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = _ => false,
            }).AllowAnonymous();
            app.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("ready"),
            }).AllowAnonymous();
            app.MapHealthChecks("/api/healthcheck", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("ready"),
            }).AllowAnonymous();

            return app;
        }

        // ----------------------------------------------------------------
        // Reflection helpers used by the auto-discovery features above.
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns every loadable type from an assembly while tolerating optional dependencies
        /// that prevented a subset of its types from loading.
        /// </summary>
        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.OfType<Type>();
            }
        }

        internal static void ValidateMappedRoutesForRuntimeRole(
            WebApplication app,
            ApiRuntimeRole? runtimeRole)
        {
            if (runtimeRole is ApiRuntimeRole.Api or ApiRuntimeRole.Combined)
                return;

            var operationalRoutes = new HashSet<string>(StringComparer.Ordinal)
            {
                "/health/live",
                "/health/ready",
                "/api/healthcheck",
            };
            var mappedRoutes = ((IEndpointRouteBuilder)app)
                .DataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Select(endpoint => endpoint.RoutePattern.RawText ?? "<unnamed>")
                .ToArray();
            var forbiddenRoutes = mappedRoutes
                .Where(pattern => !operationalRoutes.Contains(pattern))
                .Concat(mappedRoutes
                    .Where(operationalRoutes.Contains)
                    .GroupBy(pattern => pattern, StringComparer.Ordinal)
                    .Where(group => group.Count() != 1)
                    .Select(group => $"{group.Key} (mapped {group.Count()} times)"))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(pattern => pattern, StringComparer.Ordinal)
                .ToArray();

            if (forbiddenRoutes.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Runtime role '{runtimeRole}' cannot expose non-operational routes: " +
                    string.Join(", ", forbiddenRoutes));
            }
        }

        /// <summary>
        /// Eagerly loads every transitive referenced assembly of <paramref name="entry"/>
        /// so reflection-based scans can find extension methods that live in
        /// dependency projects which haven't been touched by code yet.
        /// </summary>
        private static void EnsureReferencedAssembliesLoaded(Assembly entry)
        {
            if (entry == null)
                return;

            var loaded = new HashSet<string>(
                AppDomain.CurrentDomain.GetAssemblies().Select(GetAssemblyIdentity));

            var stack = new Stack<Assembly>();
            stack.Push(entry);

            while (stack.Count > 0)
            {
                var asm = stack.Pop();
                foreach (var refName in asm.GetReferencedAssemblies())
                {
                    if (refName.Name is not { } referencedName || !loaded.Add(referencedName))
                        continue;
                    try
                    {
                        var loadedAsm = Assembly.Load(refName);
                        stack.Push(loadedAsm);
                    }
                    catch
                    {
                        // ignore unloadable references (system / native shims)
                    }
                }
            }
        }

        /// <summary>
        /// Resolves a service-owned layer by convention even when the API does not use any of
        /// its types directly. In that case the compiler intentionally omits the layer from the
        /// API assembly's metadata references, although the project output is still copied next
        /// to the executable.
        /// </summary>
        private static IEnumerable<Assembly> GetConventionAssemblies(
            Assembly entry,
            string appName,
            string layerSuffix)
        {
            var entryName = entry.GetName().Name ?? string.Empty;
            var separatorIndex = entryName.IndexOf('.');
            var entryPrefix = separatorIndex > 0 ? entryName[..separatorIndex] : entryName;
            var simpleNames = new[]
            {
                $"{appName}.{layerSuffix}",
                $"{entryPrefix}.{layerSuffix}"
            }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase);

            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var simpleName in simpleNames)
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(candidate => string.Equals(
                        candidate.GetName().Name,
                        simpleName,
                        StringComparison.OrdinalIgnoreCase));

                if (assembly == null)
                {
                    try
                    {
                        assembly = Assembly.Load(new AssemblyName(simpleName));
                    }
                    catch
                    {
                        var assemblyPath = System.IO.Path.Combine(
                            AppContext.BaseDirectory,
                            $"{simpleName}.dll");

                        if (System.IO.File.Exists(assemblyPath))
                        {
                            try
                            { assembly = Assembly.LoadFrom(assemblyPath); }
                            catch { /* handled by the caller's missing-extension error */ }
                        }
                    }
                }

                if (assembly != null && yielded.Add(GetAssemblyIdentity(assembly)))
                    yield return assembly;
            }

            if (yielded.Add(GetAssemblyIdentity(entry)))
                yield return entry;
        }

        private static string GetAssemblyIdentity(Assembly assembly) =>
            assembly.FullName
            ?? assembly.GetName().Name
            ?? throw new InvalidOperationException("An assembly without an identity cannot be discovered.");

        /// <summary>
        /// Finds the first public static extension method matching <paramref name="methodName"/>
        /// and the supplied parameter types across the candidate assemblies.
        /// </summary>
        private static (MethodInfo? Method, Assembly? Assembly) FindStaticExtension(
            IEnumerable<Assembly> assemblies,
            string methodName,
            params Type[] paramTypes)
            => FindStaticExtension(assemblies, new[] { methodName }, paramTypes);

        /// <summary>
        /// Finds the first public static extension method matching any supplied method name
        /// and the supplied parameter types across the candidate assemblies.
        /// </summary>
        private static (MethodInfo? Method, Assembly? Assembly) FindStaticExtension(
            IEnumerable<Assembly> assemblies,
            IEnumerable<string> methodNames,
            params Type[] paramTypes)
        {
            var names = new HashSet<string>(methodNames, StringComparer.Ordinal);

            foreach (var asm in assemblies)
            {
                Type[] types;
                try
                { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.OfType<Type>().ToArray(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (!(t.IsAbstract && t.IsSealed) || t.IsGenericType)
                        continue;

                    foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public))
                    {
                        if (!names.Contains(m.Name))
                            continue;
                        if (!m.IsDefined(typeof(ExtensionAttribute), false))
                            continue;
                        var pars = m.GetParameters();
                        if (pars.Length != paramTypes.Length)
                            continue;
                        var ok = true;
                        for (var i = 0; i < pars.Length; i++)
                        {
                            if (pars[i].ParameterType != paramTypes[i])
                            { ok = false; break; }
                        }
                        if (ok)
                            return (m, asm);
                    }
                }
            }
            return (null, null);
        }

        /// <summary>
        /// Finds public MVC controller types that ASP.NET Core can activate.
        /// </summary>
        private static IEnumerable<Type> DiscoverControllerTypes(Assembly entryAssembly)
        {
            foreach (var type in GetLoadableTypes(entryAssembly))
            {
                if (type == null || !type.IsClass || type.IsAbstract || type.IsGenericType)
                    continue;
                if (!(type.IsPublic || type.IsNestedPublic))
                    continue;
                if (type.IsDefined(typeof(NonControllerAttribute), inherit: true))
                    continue;

                if (typeof(ControllerBase).IsAssignableFrom(type) ||
                    type.Name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase) ||
                    type.IsDefined(typeof(ControllerAttribute), inherit: true))
                {
                    yield return type;
                }
            }
        }

        /// <summary>
        /// Discovers named authorization policies referenced by MVC authorization metadata and
        /// by route-mapping methods. Route mappers use compile-time policy constants, so reading
        /// their IL string tokens avoids invoking them twice and introducing startup side effects.
        /// </summary>
        private static string[] DiscoverAuthPolicies(
            IReadOnlyCollection<MethodInfo> routeMethods,
            IReadOnlyCollection<Type> controllerTypes)
        {
            var supportedPolicies = typeof(AuthPolicies)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(string) && field.IsLiteral)
                .Select(field => field.GetRawConstantValue() as string)
                .OfType<string>()
                .Where(policy => !string.IsNullOrWhiteSpace(policy))
                .ToHashSet(StringComparer.Ordinal);

            var discoveredPolicies = new HashSet<string>(StringComparer.Ordinal);

            foreach (var controllerType in controllerTypes)
            {
                AddAuthorizeMetadataPolicies(controllerType, supportedPolicies, discoveredPolicies);

                foreach (var method in controllerType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public))
                {
                    AddAuthorizeMetadataPolicies(method, supportedPolicies, discoveredPolicies);
                }
            }

            foreach (var mapperType in routeMethods
                .Select(method => method.DeclaringType)
                .OfType<Type>()
                .Distinct())
            {
                foreach (var method in mapperType.GetMethods(
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly))
                {
                    AddAuthorizeMetadataPolicies(method, supportedPolicies, discoveredPolicies);

                    foreach (var literal in GetMethodStringLiterals(method))
                    {
                        if (supportedPolicies.Contains(literal))
                            discoveredPolicies.Add(literal);
                    }
                }
            }

            return discoveredPolicies
                .OrderBy(policy => policy, StringComparer.Ordinal)
                .ToArray();
        }

        private static void AddAuthorizeMetadataPolicies(
            MemberInfo member,
            ISet<string> supportedPolicies,
            ISet<string> discoveredPolicies)
        {
            IEnumerable<IAuthorizeData> metadata;
            try
            {
                metadata = member
                    .GetCustomAttributes(inherit: true)
                    .OfType<IAuthorizeData>()
                    .ToArray();
            }
            catch
            {
                // An optional attribute dependency that cannot be loaded cannot contribute a
                // usable authorization policy, so discovery may safely continue.
                return;
            }

            foreach (var authorization in metadata)
            {
                var policy = authorization.Policy;
                if (!string.IsNullOrWhiteSpace(policy) &&
                    supportedPolicies.Contains(policy))
                {
                    discoveredPolicies.Add(policy);
                }
            }
        }

        /// <summary>
        /// Extracts <c>ldstr</c> operands from a method body. Policy names are subsequently
        /// intersected with the finite set supported by the shared authentication setup, making
        /// false positives from arbitrary IL bytes harmless.
        /// </summary>
        private static IEnumerable<string> GetMethodStringLiterals(MethodInfo method)
        {
            byte[]? il;
            try
            { il = method.GetMethodBody()?.GetILAsByteArray(); }
            catch { yield break; }

            if (il == null || il.Length < 5)
                yield break;

            for (var index = 0; index <= il.Length - 5; index++)
            {
                // ECMA-335: ldstr = 0x72, followed by a four-byte user-string token (0x70xxxxxx).
                if (il[index] != 0x72)
                    continue;

                var token = BitConverter.ToInt32(il, index + 1);
                if ((token & unchecked((int)0xff000000)) != 0x70000000)
                    continue;

                string literal;
                try
                { literal = method.Module.ResolveString(token); }
                catch { continue; }

                if (!string.IsNullOrWhiteSpace(literal))
                    yield return literal;

                index += 4;
            }
        }

        /// <summary>
        /// Determines whether any API-boundary assembly contains a concrete CQRS command/query.
        /// </summary>
        private static bool ContainsCqrsTypes(IEnumerable<Assembly> assemblies) =>
            assemblies
                .SelectMany(GetLoadableTypes)
                .Any(type => type is { IsClass: true, IsAbstract: false } &&
                    typeof(ICommandQuery).IsAssignableFrom(type));

        /// <summary>
        /// Discovers every public <c>Map*Routes</c> extension used by route-group mapping.
        /// </summary>
        private static IEnumerable<MethodInfo> DiscoverRouteMethods(Assembly entryAssembly)
        {
            foreach (var type in GetLoadableTypes(entryAssembly))
            {
                if (type == null || !(type.IsAbstract && type.IsSealed) || type.IsGenericType)
                    continue;

                foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    if (!method.IsDefined(typeof(ExtensionAttribute), false))
                        continue;
                    if (!method.Name.StartsWith("Map", StringComparison.Ordinal))
                        continue;
                    if (!method.Name.EndsWith("Routes", StringComparison.Ordinal))
                        continue;

                    var parameters = method.GetParameters();
                    if (parameters.Length != 1)
                        continue;
                    if (parameters[0].ParameterType != typeof(IEndpointRouteBuilder))
                        continue;

                    yield return method;
                }
            }
        }

        /// <summary>
        /// Discovers and invokes every public static extension method on
        /// <see cref="IEndpointRouteBuilder"/> whose name matches <c>Map*Routes</c>
        /// and that takes the route builder as its single parameter.
        /// </summary>
        internal static void MapDiscoveredRoutes(WebApplication app, Assembly entryAssembly)
        {
            foreach (var method in DiscoverRouteMethods(entryAssembly))
                method.Invoke(null, new object[] { app });
        }

        /// <summary>
        /// Applies EF Core schemas + raw-SQL migrations declared (or auto-discovered)
        /// via <see cref="ApiOptions.WithDatabases"/>.
        /// </summary>
        internal static async Task ApplyDatabasesAsync(
            WebApplication app,
            WithDatabasesOptions options,
            Assembly entryAssembly,
            Assembly? infraAssembly,
            CancellationToken cancellationToken)
        {
            var scanAssemblies = new[] { entryAssembly, infraAssembly }
                .OfType<Assembly>()
                .Distinct()
                .ToArray();

            // ---- EF Core: ApplySchemaForAllAsync(contexts) ----
            var contexts = options.Contexts is { Length: > 0 }
                ? options.Contexts
                : EfCoreMigrationsExtensions.DiscoverContexts(scanAssemblies).ToArray();

            if (contexts.Length > 0)
                await app.ApplySchemaForAllAsync(contexts, cancellationToken);

            // ---- Raw SQL: ApplyMySqlMigrationsForAsync(configurations) ----
            var migrationConfigs = options.MigrationConfigurations is { Length: > 0 }
                ? options.MigrationConfigurations
                : DatabaseMigrationExtensions.DiscoverMigrationConfigurations(scanAssemblies).ToArray();

            if (migrationConfigs.Length > 0)
                await app.ApplyMySqlMigrationsForAsync(migrationConfigs, cancellationToken);
        }

        /// <summary>
        /// Discovers concrete <see cref="IHostedService"/> implementations declared
        /// inside <paramref name="entryAssembly"/>.
        /// </summary>
        private static IEnumerable<Type> DiscoverHostedServiceTypes(Assembly entryAssembly)
        {
            foreach (var t in GetLoadableTypes(entryAssembly))
            {
                if (t == null)
                    continue;
                if (t.IsAbstract || t.IsInterface)
                    continue;
                if (!typeof(IHostedService).IsAssignableFrom(t))
                    continue;
                yield return t;
            }
        }
    }
}
