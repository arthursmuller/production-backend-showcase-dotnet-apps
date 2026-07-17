using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CommomStartupExtensions;
using Cqrs.Extensions;
using EfCoreMigrations;
using MessageBrokerCore.Abstractions;
using MessageBrokerRabbitMq;
using MessageBrokerRabbitMqExtensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MySqlMigrations;
using Scalar.AspNetCore;
using StorageProvider;

namespace InspacyApiHost
{
    /// <summary>
    /// Message-broker transports supported by the shared API host.
    /// </summary>
    public enum MessageBrokers
    {
        None = 0,
        RabbitMQ = 1,
    }

    /// <summary>
    /// Toggles CQRS handler registration for the host.
    /// Pass a non-null instance to enable; the host will discover handlers
    /// in the entry assembly, the auto-detected application and infrastructure assemblies and
    /// any <see cref="AdditionalAssemblies"/>.
    /// </summary>
    public sealed class CqrsOptions
    {
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
    ///   <item><see cref="EfCoreMigrationsExtensions.ApplySchema(Microsoft.AspNetCore.Builder.IApplicationBuilder, Type)"/>
    ///         for every entry in <see cref="Contexts"/> (auto-discovered when null/empty).</item>
    ///   <item><see cref="DatabaseMigrationExtensions.ApplyMySqlMigrationsFor"/> for every
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
        public Type[] Contexts { get; set; }

        /// <summary>
        /// Explicit list of <see cref="IMigrationsConfiguration"/> implementations whose raw-SQL
        /// migrations must be applied at startup. Leave null/empty to auto-discover.
        /// </summary>
        public Type[] MigrationConfigurations { get; set; }

        /// <summary>
        /// When <c>true</c> (default), this options object causes the host to apply schemas /
        /// migrations on startup. Set to <c>false</c> to opt out without nulling the property.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }


    /// <summary>
    /// Options describing an Inspacy API host (cors policy, app name, auth policies).
    /// </summary>
    public sealed class ApiOptions
    {
        public string AppName { get; set; }
        public string CorsPolicyName
        {
            get => string.IsNullOrEmpty(field) ? $"{AppName}Service" : field;
            set => field = value;
        }
        public string ApiVersion { get; set; } = "v1";
        public string[] AllowedCorsDomains { get; set; }
        public string[] AuthPolicies { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Optional. Used by reflection-based discovery as the starting assembly
        /// for routes / jobs / infra scanning. Defaults to <see cref="Assembly.GetEntryAssembly"/>.
        /// </summary>
        public Assembly EntryAssembly { get; set; }

        /// <summary>
        /// When <c>true</c>, scans the entry assembly's referenced assemblies for a static
        /// <c>IServiceCollection ConfigureInfraestructure(this IServiceCollection, IConfiguration)</c>
        /// extension method (typically defined in the matching <c>*.Infra*</c> project) and invokes it.
        /// </summary>
        public bool WithInfra { get; set; }

        /// <summary>
        /// When <c>true</c>, scans the entry assembly's referenced assemblies for a static
        /// <c>IServiceCollection ConfigureApplication(this IServiceCollection, IConfiguration)</c>
        /// extension method (typically defined in the matching <c>*.Application</c> project) and invokes it.
        /// The discovered assembly is also included in automatic CQRS registration.
        /// </summary>
        public bool WithApplication { get; set; }

        /// <summary>
        /// When non-null, registers CQRS handlers via <see cref="CqrsExtensions.RegisterCQRS"/>
        /// for the entry assembly + the auto-detected application and infrastructure assemblies + any
        /// <see cref="CqrsOptions.AdditionalAssemblies"/>.
        /// </summary>
        public CqrsOptions WithCqrs { get; set; }

        /// <summary>
        /// When <c>true</c>, scans the entry assembly for every static extension method whose
        /// name matches <c>Map*Routes</c> on <see cref="IEndpointRouteBuilder"/> and invokes
        /// each one against the built <see cref="WebApplication"/>. Defaults to <c>true</c>.
        /// May be combined with <see cref="WithControllers"/> so an API can mix attribute-routed
        /// controllers (e.g. for <c>[FromForm]</c> endpoints) with minimal-API route groups.
        /// If neither <see cref="WithControllers"/> nor this flag is enabled the host falls
        /// back to mapping route groups by default.
        /// </summary>
        public bool WithRouteGroups { get; set; } = true;

        /// <summary>
        /// When <c>true</c>, the host calls <c>app.MapControllers()</c> at build time so attribute-routed
        /// MVC controllers in the entry assembly are wired up. May be combined with
        /// <see cref="WithRouteGroups"/> so an API can expose both controllers and minimal-API
        /// endpoints from the same host.
        /// </summary>
        public bool WithControllers { get; set; }

        /// <summary>
        /// Selects the message-broker transport used by this API. Selecting
        /// <see cref="MessageBrokers.RabbitMQ"/> configures the RabbitMQ connection,
        /// discovers consumers in the entry assembly's <c>*.AppInterfaces.Queues*</c>
        /// namespaces, and subscribes them after the application is built.
        /// </summary>
        public MessageBrokers WithQueues { get; set; } = MessageBrokers.None;

        /// <summary>
        /// When <c>true</c>, scans the entry assembly for every concrete <see cref="IHostedService"/>
        /// implementation and registers it via <c>services.AddHostedService</c>. If a static
        /// <c>IServiceCollection ConfigureJobs(this IServiceCollection)</c> extension exists in
        /// the entry assembly it is invoked instead.
        /// </summary>
        public bool WithJobs { get; set; }

        /// <summary>
        /// Configures startup-time database schema/migration application.
        /// Defaults to an enabled instance using full auto-discovery; set
        /// <see cref="WithDatabasesOptions.Enabled"/> to <c>false</c> to disable.
        /// </summary>
        public WithDatabasesOptions WithDatabases { get; init; } = new();


        /// <summary>
        /// Optional callback executed at the end of <see cref="InspacyApiHostExtensions.Api"/>
        /// (after the standard service registration and after <see cref="WithInfra"/> /
        /// <see cref="WithCqrs"/> / <see cref="WithJobs"/>). Use this to add
        /// service-collection registrations not covered by the standard host
        /// (e.g. <c>builder.Services.AddMediator(...)</c>).
        /// </summary>
        public Action<WebApplicationBuilder> ConfigureServices { get; set; }

        /// <summary>
        /// Optional callback executed inside <see cref="InspacyApiBuilder.Build"/>
        /// after <c>UseInspacyApi</c> (CORS / AuthN / AuthZ / swagger / healthcheck /
        /// MapControllers) and after <see cref="ExceptionHandler"/> has been wired,
        /// but BEFORE route discovery, database migrations and queue consumer subscription.
        /// Use this to register additional middleware in the request pipeline.
        /// </summary>
        public Action<WebApplication> ConfigurePipeline { get; set; }

        /// <summary>
        /// Optional callback wired via <c>app.UseExceptionHandler(errorApp =&gt; ExceptionHandler(errorApp))</c>
        /// inside <see cref="InspacyApiBuilder.Build"/>. Registered AFTER the standard
        /// <c>UseInspacyApi</c> middleware (CORS / AuthN / AuthZ) so authentication errors
        /// short-circuit normally, but BEFORE any user-supplied <see cref="ConfigurePipeline"/>
        /// and the endpoints terminal middleware so it wraps both controller and minimal-API
        /// invocations.
        /// </summary>
        public Action<IApplicationBuilder> ExceptionHandler { get; set; }
    }

    /// <summary>
    /// Returned by <see cref="InspacyApiHostExtensions.Api"/> to allow chaining <see cref="RunApp"/>.
    /// </summary>
    public sealed class InspacyApiBuilder
    {
        private readonly ApiOptions _options;
        private readonly Assembly _entryAssembly;
        private readonly Assembly _infraAssembly;

        /// <summary>The underlying <see cref="WebApplicationBuilder"/>.</summary>
        public WebApplicationBuilder Builder { get; }

        internal InspacyApiBuilder(
            WebApplicationBuilder builder,
            ApiOptions options,
            Assembly entryAssembly,
            Assembly infraAssembly)
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
        /// is <c>true</c>), subscribes message-broker consumers when
        /// <see cref="ApiOptions.WithQueues"/> is enabled, and applies database schemas /
        /// SQL migrations when <see cref="ApiOptions.WithDatabases"/> is enabled.
        /// </summary>
        /// <returns>The fully configured <see cref="WebApplication"/> – callers may add
        /// further middleware before invoking <see cref="RunApp()"/> or
        /// <c>app.RunWithTryCatch(appName)</c>.</returns>
        public WebApplication Build()
        {
            var app = Builder.Build();
            app.UseInspacyApi(_options.CorsPolicyName);

            // Treat empty values as null for minimal apis endpoints that receives a query object, waiting for default suport on model binders
            if(_options.WithRouteGroups)
            {
                app.Use(async (context, next) =>
                {
                    var query = context.Request.Query;

                    var hasEmpty = query.Any(kvp => kvp.Value.Any(v => string.IsNullOrWhiteSpace(v)));

                    if (hasEmpty)
                    {
                        var filtered = query
                            .SelectMany(kvp => kvp.Value
                                .Where(v => !string.IsNullOrWhiteSpace(v))
                                .Select(v => new KeyValuePair<string, string?>(kvp.Key, v)))
                            .ToList();

                        context.Request.QueryString = QueryString.Create(filtered);
                    }

                    await next();
                });
            }

            // Wire the user-supplied global exception handler (if any) right after the standard
            // middleware so it wraps controllers, discovered routes and any pipeline middleware
            // added below. Registered before the endpoints terminal middleware fires.
            if (_options.ExceptionHandler != null)
                app.UseExceptionHandler(errorApp => _options.ExceptionHandler(errorApp));

            // User-supplied middleware extension point (UseRouting-time additions, custom auth,
            // request logging, etc.). Runs after exception handling so unhandled errors are caught.
            _options.ConfigurePipeline?.Invoke(app);

            // ---- Routing ----
            // Controllers and route groups are independent: an API may opt into either,
            // both (mixed mode for [FromForm] endpoints living in controllers next to
            // minimal-API groups), or fall back to route groups when neither flag is set.
            var withControllers = _options.WithControllers;
            var withRouteGroups = _options.WithRouteGroups || !withControllers;

            if (withControllers)
                app.MapControllers();

            if (withRouteGroups)
                InspacyApiHostExtensions.MapDiscoveredRoutes(app, _entryAssembly);

            // Databases MUST be applied before queue consumers subscribe and before the host
            // starts (which is what kicks off any registered IHostedService jobs). Otherwise
            // consumers / jobs could begin processing against a not-yet-migrated schema.
            if (_options.WithDatabases is { Enabled: true })
                InspacyApiHostExtensions.ApplyDatabases(app, _options.WithDatabases, _entryAssembly, _infraAssembly);

            if (_options.WithQueues == MessageBrokers.RabbitMQ)
                RabbitMqBrokerExtensions.ConfigureConsumers(app);

            return app;
        }

        /// <summary>
        /// Calls <see cref="Build"/> then runs the host (blocks until shutdown).
        /// </summary>
        public void RunApp() => Build().RunWithTryCatch(_options.AppName);

        /// <summary>
        /// Convenience overload accepting an explicit app-name. Currently informational
        /// (mirrors the other Inspacy services); the underlying behaviour is identical to
        /// the parameterless <see cref="RunApp()"/>.
        /// </summary>
        public void RunApp(string appName)
        {
            if (!string.IsNullOrWhiteSpace(appName))
                _options.AppName = appName;
            RunApp();
        }
    }

    /// <summary>
    /// One-shot entry point for an Inspacy API host.
    /// <para>
    /// Replaces the boilerplate trio
    /// <c>var builder = WebApplication.CreateBuilder(args); builder.Api(opts).RunApp();</c>
    /// with a single call: <c>ApiBuilder.New(new ApiOptions { ... });</c>
    /// </para>
    /// <para>
    /// Internally creates a <see cref="WebApplicationBuilder"/> using
    /// <see cref="Environment.GetCommandLineArgs"/> (skipping argv[0]), wires the standard
    /// Inspacy services / middleware via <see cref="InspacyApiHostExtensions.Api"/>, and then
    /// blocks on <see cref="InspacyApiBuilder.RunApp()"/> until shutdown.
    /// </para>
    /// </summary>
    public static class ApiBuilder
    {
        /// <summary>
        /// Builds, configures and runs an Inspacy API host. Blocks until the host shuts down.
        /// </summary>
        public static void New(ApiOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            // Skip the executable path (argv[0]) so behaviour matches `WebApplication.CreateBuilder(args)`.
            var commandLineArgs = Environment.GetCommandLineArgs();
            var args = commandLineArgs.Length > 1 ? commandLineArgs[1..] : Array.Empty<string>();

            var builder = WebApplication.CreateBuilder(args);
            builder.Api(options).RunApp();
        }
    }

    /// <summary>
    /// Default CORS domains used by every Inspacy API.
    /// </summary>
    public static class InspacyDefaults
    {
        public static string[] DefaultCorsDomains() => new[]
        {
            "https://www.inspacy.com.br",
            "https://www.inspacy.com",
            "http://localhost",
            $"{Environment.GetEnvironmentVariable("FRONT_URL")}",
            $"{Environment.GetEnvironmentVariable("FRONT_URL")}:8081",
            $"{Environment.GetEnvironmentVariable("FRONT_URL")}:8082",
            $"{Environment.GetEnvironmentVariable("FRONT_URL")}:3001",
            $"{Environment.GetEnvironmentVariable("FRONT_URL")}:3000",
            $"{Environment.GetEnvironmentVariable("USER_API_URL")}:3000",
        };
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
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.AppName)) throw new ArgumentException("AppName is required", nameof(options));
            if (string.IsNullOrWhiteSpace(options.CorsPolicyName)) throw new ArgumentException("CorsPolicyName is required", nameof(options));

            var corsDomains = options.AllowedCorsDomains ?? InspacyDefaults.DefaultCorsDomains();

            builder.ConfigureLogger(options.AppName);

            builder.Services.ConfigureCommomExtensions(
                new Cors(options.CorsPolicyName, corsDomains),
                $"{options.AppName}.API",
                options.ApiVersion ?? "v1",
                options.AuthPolicies ?? Array.Empty<string>());

            var entryAssembly = options.EntryAssembly ?? Assembly.GetEntryAssembly();
            EnsureReferencedAssembliesLoaded(entryAssembly);

            // ---- WithQueues : configure the selected transport and discover API consumers ----
            switch (options.WithQueues)
            {
                case MessageBrokers.None:
                    break;
                case MessageBrokers.RabbitMQ:
                    builder.Services.RMQConfigureConnection(builder.Configuration);
                    RegisterRabbitMqConsumers(builder.Services, entryAssembly);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(options.WithQueues),
                        options.WithQueues,
                        "Unsupported message-broker transport.");
            }

            // ---- WithApplication : reflect ConfigureApplication(IServiceCollection, IConfiguration) ----
            Assembly applicationAssembly = null;
            if (options.WithApplication)
            {
                var (method, asm) = FindStaticExtension(
                    GetCandidateAssemblies(entryAssembly),
                    "ConfigureApplication",
                    typeof(IServiceCollection),
                    typeof(IConfiguration));

                if (method == null)
                    throw new InvalidOperationException(
                        "WithApplication=true but no static `IServiceCollection ConfigureApplication(this IServiceCollection, IConfiguration)` extension was found in any referenced assembly.");

                applicationAssembly = asm;
                method.Invoke(null, new object[] { builder.Services, builder.Configuration });
            }

            // ---- WithInfra : reflect ConfigureInfraestructure(IServiceCollection, IConfiguration) ----
            Assembly infraAssembly = null;
            if (options.WithInfra)
            {
                var (method, asm) = FindStaticExtension(
                    GetCandidateAssemblies(entryAssembly),
                    "ConfigureInfraestructure",
                    typeof(IServiceCollection),
                    typeof(IConfiguration));

                if (method == null)
                    throw new InvalidOperationException(
                        "WithInfra=true but no static `IServiceCollection ConfigureInfraestructure(this IServiceCollection, IConfiguration)` extension was found in any referenced assembly.");

                infraAssembly = asm;
                method.Invoke(null, new object[] { builder.Services, builder.Configuration });
            }

            // ---- WithCqrs : RegisterCQRS for entry + application + infra (+ AdditionalAssemblies) ----
            if (options.WithCqrs != null)
            {
                var cqrsAssemblies = new[] { entryAssembly, applicationAssembly, infraAssembly }
                    .Concat(options.WithCqrs.AdditionalAssemblies ?? Array.Empty<Assembly>())
                    .Where(assembly => assembly != null)
                    .Distinct();

                foreach (var assembly in cqrsAssemblies)
                    builder.Services.RegisterCQRS(assembly);
            }

            // ---- WithJobs : ConfigureJobs(IServiceCollection) extension OR auto IHostedService discovery ----
            if (options.WithJobs)
            {
                var (jobsMethod, _) = FindStaticExtension(
                    new[] { entryAssembly },
                    "ConfigureJobs",
                    typeof(IServiceCollection));

                if (jobsMethod != null)
                {
                    jobsMethod.Invoke(null, new object[] { builder.Services });
                }
                else
                {
                    foreach (var hostedType in DiscoverHostedServiceTypes(entryAssembly))
                        builder.Services.AddSingleton(typeof(IHostedService), hostedType);
                }
            }

            // ---- User extension point: arbitrary additional service registrations ----
            options.ConfigureServices?.Invoke(builder);

            return new InspacyApiBuilder(builder, options, entryAssembly, infraAssembly);
        }

        /// <summary>
        /// Registers concrete RabbitMQ consumers owned by the API boundary. Consumers receive
        /// the configured retry count as an explicit constructor argument; all other constructor
        /// dependencies (including <c>IServiceProvider</c> and <c>ILogger&lt;T&gt;</c>) come from DI.
        /// </summary>
        private static void RegisterRabbitMqConsumers(IServiceCollection services, Assembly entryAssembly)
        {
            foreach (var consumerType in GetLoadableTypes(entryAssembly)
                .Where(type => type is { IsClass: true, IsAbstract: false }
                    && typeof(MessageBrokerRabbitMQ).IsAssignableFrom(type)
                    && typeof(IMessageBroker).IsAssignableFrom(type)
                    && type.Namespace?.Contains(".AppInterfaces.Queues", StringComparison.Ordinal) == true))
            {
                services.AddSingleton(typeof(IMessageBroker), serviceProvider =>
                {
                    var retryCount = serviceProvider
                        .GetRequiredService<PublicRmqConnectionConfiguration>()
                        .RetryCount;

                    return ActivatorUtilities.CreateInstance(serviceProvider, consumerType, retryCount);
                });
            }
        }

        /// <summary>
        /// Wires up the standard middleware pipeline used by Inspacy APIs:
        /// CORS, AuthN/AuthZ, OpenAPI document + Scalar API reference (Development) and the
        /// /api/healthcheck endpoint.
        /// </summary>
        public static WebApplication UseInspacyApi(this WebApplication app, string corsPolicyName)
        {
            if (string.IsNullOrWhiteSpace(corsPolicyName)) throw new ArgumentException("corsPolicyName is required", nameof(corsPolicyName));

            app.UseCors(corsPolicyName)
               .UseAuthentication()
               .UseAuthorization();

            if (app.Environment.IsDevelopment())
            {
                // /openapi/{documentName}.json (e.g. /openapi/v1.json).
                app.MapOpenApi();
                // Scalar UI mounted at /scalar/{documentName}.
                app.MapScalarApiReference(options =>
                {
                    options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
                });
            }

            app.MapGet("/api/healthcheck", () => "Healthy").AllowAnonymous();

            // Anonymous storage-provider smoke test endpoint. Lets the deploy scripts
            // verify (per-API) that the configured IStorageProviderService (OCI in prod,
            // MinIO in dev) is reachable and the credentials work end-to-end. Returns 503
            // when the host did not register a storage provider.
            app.MapPost("/api/storage-test", async (HttpRequest req, IServiceProvider sp) =>
            {
                var storage = sp.GetService<IStorageProviderService>();
                if (storage is null)
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                if (!req.HasFormContentType)
                    return Results.BadRequest(new { error = "multipart/form-data required" });
                var form = await req.ReadFormAsync();
                var file = form.Files.FirstOrDefault();
                if (file is null)
                    return Results.BadRequest(new { error = "file required (form field name does not matter)" });
                await using var stream = file.OpenReadStream();
                var url = await storage.UploadStream("storage-test", file.FileName, stream, null, file.ContentType);
                return Results.Ok(new { ok = true, url, bytes = file.Length, contentType = file.ContentType });
            }).AllowAnonymous().DisableAntiforgery();

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
            if (assembly == null)
                return Enumerable.Empty<Type>();

            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null);
            }
        }

        /// <summary>
        /// Eagerly loads every transitive referenced assembly of <paramref name="entry"/>
        /// so reflection-based scans can find extension methods that live in
        /// dependency projects which haven't been touched by code yet.
        /// </summary>
        private static void EnsureReferencedAssembliesLoaded(Assembly entry)
        {
            if (entry == null) return;

            var loaded = new HashSet<string>(
                AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name));

            var stack = new Stack<Assembly>();
            stack.Push(entry);

            while (stack.Count > 0)
            {
                var asm = stack.Pop();
                foreach (var refName in asm.GetReferencedAssemblies())
                {
                    if (!loaded.Add(refName.Name)) continue;
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
        /// Returns the entry assembly first followed by every assembly currently
        /// loaded into the <see cref="AppDomain"/> (skipping framework/system ones).
        /// </summary>
        private static IEnumerable<Assembly> GetCandidateAssemblies(Assembly entry)
        {
            yield return entry;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm == entry) continue;
                var name = asm.GetName().Name ?? string.Empty;
                if (name.StartsWith("System.", StringComparison.Ordinal)) continue;
                if (name.StartsWith("Microsoft.", StringComparison.Ordinal)) continue;
                if (name.StartsWith("netstandard", StringComparison.Ordinal)) continue;
                if (name == "mscorlib" || name == "WindowsBase") continue;
                yield return asm;
            }
        }

        /// <summary>
        /// Finds the first public static extension method matching <paramref name="methodName"/>
        /// and the supplied parameter types across the candidate assemblies.
        /// </summary>
        private static (MethodInfo Method, Assembly Assembly) FindStaticExtension(
            IEnumerable<Assembly> assemblies,
            string methodName,
            params Type[] paramTypes)
        {
            foreach (var asm in assemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (!(t.IsAbstract && t.IsSealed) || t.IsGenericType) continue;

                    foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public))
                    {
                        if (m.Name != methodName) continue;
                        if (!m.IsDefined(typeof(ExtensionAttribute), false)) continue;
                        var pars = m.GetParameters();
                        if (pars.Length != paramTypes.Length) continue;
                        var ok = true;
                        for (var i = 0; i < pars.Length; i++)
                        {
                            if (pars[i].ParameterType != paramTypes[i]) { ok = false; break; }
                        }
                        if (ok) return (m, asm);
                    }
                }
            }
            return (null, null);
        }

        /// <summary>
        /// Discovers and invokes every public static extension method on
        /// <see cref="IEndpointRouteBuilder"/> whose name matches <c>Map*Routes</c>
        /// and that takes the route builder as its single parameter.
        /// </summary>
        internal static void MapDiscoveredRoutes(WebApplication app, Assembly entryAssembly)
        {
            Type[] types;
            try { types = entryAssembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

            foreach (var t in types)
            {
                if (t == null) continue;
                if (!(t.IsAbstract && t.IsSealed) || t.IsGenericType) continue;

                foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    if (!m.IsDefined(typeof(ExtensionAttribute), false)) continue;
                    if (!m.Name.StartsWith("Map", StringComparison.Ordinal)) continue;
                    if (!m.Name.EndsWith("Routes", StringComparison.Ordinal)) continue;
                    var pars = m.GetParameters();
                    if (pars.Length != 1) continue;
                    if (pars[0].ParameterType != typeof(IEndpointRouteBuilder)) continue;

                    m.Invoke(null, new object[] { app });
                }
            }
        }

        /// <summary>
        /// Applies EF Core schemas + raw-SQL migrations declared (or auto-discovered)
        /// via <see cref="ApiOptions.WithDatabases"/>.
        /// </summary>
        internal static void ApplyDatabases(
            WebApplication app,
            WithDatabasesOptions options,
            Assembly entryAssembly,
            Assembly infraAssembly)
        {
            var scanAssemblies = new[] { entryAssembly, infraAssembly }
                .Where(a => a != null)
                .Distinct()
                .ToArray();

            // ---- EF Core: ApplySchema(context) ----
            var contexts = options.Contexts is { Length: > 0 }
                ? options.Contexts
                : EfCoreMigrationsExtensions.DiscoverContexts(scanAssemblies).ToArray();

            if (contexts.Length > 0)
                app.ApplySchemaForAll(contexts);

            // ---- Raw SQL: ApplyMySqlMigrations(IMigrationsConfiguration) ----
            var migrationConfigs = options.MigrationConfigurations is { Length: > 0 }
                ? options.MigrationConfigurations
                : DatabaseMigrationExtensions.DiscoverMigrationConfigurations(scanAssemblies).ToArray();

            if (migrationConfigs.Length > 0)
                app.ApplyMySqlMigrationsFor(migrationConfigs);
        }

        /// <summary>
        /// Discovers concrete <see cref="IHostedService"/> implementations declared
        /// inside <paramref name="entryAssembly"/>. Used as a fallback when no
        /// <c>ConfigureJobs</c> extension method is present.
        /// </summary>
        private static IEnumerable<Type> DiscoverHostedServiceTypes(Assembly entryAssembly)
        {
            Type[] types;
            try { types = entryAssembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

            foreach (var t in types)
            {
                if (t == null) continue;
                if (t.IsAbstract || t.IsInterface) continue;
                if (!typeof(IHostedService).IsAssignableFrom(t)) continue;
                yield return t;
            }
        }
    }
}
