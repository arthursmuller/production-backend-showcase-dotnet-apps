using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Moderation.API;
using Moderation.API.AppInterfaces.Routes;
using Moderation.API.AppInterfaces.Jobs;
using Moderation.Application.Features.Cases.Commands;
using Moderation.Application.Features.Cases.Queries;
using Moderation.Application.Features.Exports;
using Moderation.Application.Features.Exports.Queries;
using Moderation.Application.Features.Reports.Commands;
using Moderation.Application.Features.Reports.Queries;
using Moderation.Application.Dtos;
using Moderation.Application.Options;
using Moderation.Contracts;
using Moderation.Contracts.Restricted;

namespace Moderation.UnitTests;

public sealed class ModerationSecurityAndRoutesTests
{
    private static readonly (string Policy, string Role)[] RoleBoundaries =
    [
        (ModerationPolicies.Reporter, "ModerationReporter"),
        (ModerationPolicies.Triage, "ModerationTriage"),
        (ModerationPolicies.Investigator, "ModerationInvestigator"),
        (ModerationPolicies.Decision, "ModerationDecision"),
        (ModerationPolicies.Admin, "ModerationAdmin"),
    ];

    [Fact]
    public async Task Authorization_policies_isolate_every_moderation_role()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddModerationAuthorization();
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        foreach (var (policy, _) in RoleBoundaries)
            foreach (var (_, role) in RoleBoundaries)
            {
                var principal = Principal(role);
                var result = await authorization.AuthorizeAsync(principal, null, policy);
                Assert.Equal(policy == role, result.Succeeded);
            }

        Assert.True((await authorization.AuthorizeAsync(Principal("SysAdmin"), null, ModerationPolicies.Admin)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(Principal("SysAdmin"), null, ModerationPolicies.Decision)).Succeeded);

        var service = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("client_id", "feed-projection"),
            new Claim("scope", "moderation.decision.export"),
        ], "test"));
        var humanRole = Principal("ModerationAdmin");
        Assert.True((await authorization.AuthorizeAsync(
            service, null, ModerationPolicies.DecisionExport)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(
            humanRole, null, ModerationPolicies.DecisionExport)).Succeeded);
    }

    [Fact]
    public void Every_route_has_the_expected_reporter_or_internal_role_policy()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        builder.Services.AddModerationAuthorization();
        builder.Services.AddScoped<ReportCommands>();
        builder.Services.AddScoped<ReportQueries>();
        builder.Services.AddScoped<CaseCommands>();
        builder.Services.AddScoped<CaseQueries>();
        builder.Services.AddScoped<CaseMessageCommands>();
        builder.Services.AddScoped<LegalHoldCommands>();
        using var app = builder.Build();
        app.MapModerationRoutes();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        Assert.Equal(22, endpoints.Length);

        foreach (var endpoint in endpoints)
        {
            var route = endpoint.RoutePattern.RawText!;
            var policy = Assert.Single(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                data => !string.IsNullOrWhiteSpace(data.Policy)).Policy;
            var expected = route switch
            {
                var value when value.StartsWith("/api/moderation", StringComparison.Ordinal) => ModerationPolicies.Reporter,
                "/internal/moderation/decision-state" => ModerationPolicies.DecisionExport,
                var value when value.Contains("/triage/", StringComparison.Ordinal) => ModerationPolicies.Triage,
                var value when value.Contains("/investigation/", StringComparison.Ordinal) => ModerationPolicies.Investigator,
                var value when value.Contains("/decision/", StringComparison.Ordinal) => ModerationPolicies.Decision,
                var value when value.Contains("/admin/", StringComparison.Ordinal) => ModerationPolicies.Admin,
                _ => throw new Xunit.Sdk.XunitException($"Unclassified moderation route: {route}"),
            };
            Assert.Equal(expected, policy);
        }
    }

    [Fact]
    public void All_routes_publish_no_domain_or_broker_top_level_http_contracts()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        builder.Services.AddModerationAuthorization();
        using var app = builder.Build();
        app.MapModerationRoutes();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        var reporterEndpoints = endpoints
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/api/moderation",
                StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(4, reporterEndpoints.Length);

        var accepts = endpoints
            .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<IAcceptsMetadata>())
            .Select(metadata => metadata.RequestType)
            .Where(type => type is not null)
            .Cast<Type>()
            .ToArray();
        var produces = endpoints
            .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>())
            .Select(metadata => metadata.Type)
            .Where(type => type is not null)
            .Cast<Type>()
            .ToArray();
        var boundaryTypes = accepts.Concat(produces).Distinct().ToArray();

        Assert.Contains(typeof(SubmitModerationReportRequestDto), accepts);
        Assert.Contains(typeof(CaseTransitionRequest), accepts);
        Assert.Contains(typeof(ModerationReportReceiptDto), produces);
        Assert.Contains(typeof(ModerationReportStatusDto), produces);
        Assert.DoesNotContain(
            boundaryTypes,
            type => type.Assembly.GetName().Name is "Moderation.Domain" or "MessageBrokerContracts");
        Assert.DoesNotContain(
            boundaryTypes,
            type => type.Namespace?.StartsWith("Moderation.Domain", StringComparison.Ordinal) == true
                || type.Namespace?.StartsWith("Moderation.Contracts", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Route_handlers_expose_typed_result_unions()
    {
        var handlers = typeof(ModerationRoutes)
            .GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Where(method => method.ReturnType.IsGenericType
                && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            .Where(method => method.Name is not "Problem")
            .ToArray();

        Assert.NotEmpty(handlers);
        Assert.All(handlers, handler =>
            Assert.Contains("Results`2", handler.ReturnType.GenericTypeArguments[0].Name, StringComparison.Ordinal));
    }

    [Fact]
    public void Conditional_write_parser_requires_a_strong_If_Match_and_stale_versions_map_to_412()
    {
        var parser = typeof(ModerationRoutes).GetMethod(
            "TryExpectedVersion",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var missingContext = new DefaultHttpContext();
        var missingArguments = new object?[] { missingContext, 0L, null };
        Assert.False((bool)parser.Invoke(null, missingArguments)!);
        Assert.Equal(
            StatusCodes.Status428PreconditionRequired,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(missingArguments[2]).StatusCode);

        var strongContext = new DefaultHttpContext();
        strongContext.Request.Headers.IfMatch = "\"7\"";
        var strongArguments = new object?[] { strongContext, 0L, null };
        Assert.True((bool)parser.Invoke(null, strongArguments)!);
        Assert.Equal(7L, strongArguments[1]);

        var weakContext = new DefaultHttpContext();
        weakContext.Request.Headers.IfMatch = "W/\"7\"";
        var weakArguments = new object?[] { weakContext, 0L, null };
        Assert.False((bool)parser.Invoke(null, weakArguments)!);
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(weakArguments[2]).StatusCode);

        var mapper = typeof(ModerationRoutes).GetMethod(
            "Problem",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var stale = mapper.Invoke(null,
        [
            new Moderation.Application.Dtos.ModerationFailure(
                Moderation.Application.Dtos.ModerationFailureKind.VersionConflict,
                "version-conflict",
                "reload",
                8),
        ]);
        Assert.Equal(
            StatusCodes.Status412PreconditionFailed,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(stale).StatusCode);
    }

    [Fact]
    public void Public_decision_and_action_events_exclude_sensitive_fields()
    {
        var prohibitedFragments = new[]
        {
            "reporter", "evidence", "reason", "actor", "assignment", "investigator",
            "description", "content", "text", "storage", "legalhold",
        };
        var publicEventTypes = new[]
        {
            typeof(ModerationDecisionMade),
            typeof(ModerationActionApplied),
        };

        foreach (var type in publicEventTypes)
            foreach (var property in type.GetProperties())
                foreach (var prohibited in prohibitedFragments)
                    Assert.DoesNotContain(prohibited, property.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Retention_and_job_defaults_match_engineering_policy_and_validate_fail_closed()
    {
        var retention = new ModerationRetentionOptions();
        var escalation = new ModerationEscalationOptions();
        var sla = new ModerationSlaOptions();

        Assert.Equal(730, retention.ReportsAndCasesDays);
        Assert.Equal(2555, retention.AuditDays);
        Assert.False(retention.Enabled);
        Assert.True(ModerationRetentionOptions.Validate(retention));
        Assert.False(ModerationRetentionOptions.Validate(new ModerationRetentionOptions
        {
            ReportsAndCasesDays = 729,
            AuditDays = 2555,
        }));
        Assert.False(ModerationRetentionOptions.Validate(new ModerationRetentionOptions
        {
            Enabled = true,
            ApprovalReference = string.Empty,
        }));
        Assert.True(ModerationRetentionOptions.Validate(new ModerationRetentionOptions
        {
            Enabled = true,
            ApprovalReference = "legal:retention-policy/2026-07",
        }));
        Assert.True(ModerationEscalationOptions.Validate(escalation));
        Assert.Equal(240, sla.ReceivedMinutes);
        Assert.True(ModerationSlaOptions.Validate(sla));
        Assert.False(ModerationSlaOptions.Validate(new ModerationSlaOptions
        {
            ReceivedMinutes = 241,
        }));
        Assert.False(ModerationEscalationOptions.Validate(new ModerationEscalationOptions
        {
            MaxBatchSize = 501,
            PollIntervalSeconds = 30,
        }));
    }

    [Fact]
    public void Sla_worker_is_a_concrete_discoverable_background_service_without_a_job_extension()
    {
        Assert.True(typeof(BackgroundService).IsAssignableFrom(typeof(ModerationSlaEscalationJob)));
        Assert.True(typeof(BackgroundService).IsAssignableFrom(typeof(ModerationRetentionJob)));
        Assert.Null(typeof(ModerationSlaEscalationJob).Assembly.GetType(
            "Moderation.API.AppInterfaces.Jobs.JobsExtensions"));
    }

    [Theory]
    [InlineData("ConsumerWorker")]
    [InlineData("JobWorker")]
    [InlineData("Migration")]
    public void Non_request_service_entry_roles_do_not_register_or_require_decision_cursor_secrets(
        string role)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Runtime:Role"] = role,
            ["DecisionExport:CursorSigningKey"] = string.Empty,
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddModerationAuthorization(configuration);

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(ModerationDecisionExportCursor));
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(ExportModerationDecisionStateQuery));
    }

    [Theory]
    [InlineData("Api")]
    [InlineData("Combined")]
    public void Request_service_entry_roles_register_and_validate_decision_cursor_secrets(string role)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Runtime:Role"] = role,
            ["DecisionExport:CursorSigningKey"] = Convert.ToBase64String(new byte[32]),
            ["DecisionExport:CursorLifetimeMinutes"] = "360",
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddModerationAuthorization(configuration);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ExportModerationDecisionStateQuery));
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ModerationDecisionExportCursor>());
        Assert.True(ModerationDecisionExportOptions.IsValid(
            provider.GetRequiredService<IOptions<ModerationDecisionExportOptions>>().Value));
    }

    [Fact]
    public void Local_and_production_hosts_wire_api_only_decision_cursor_secret_sources()
    {
        var root = FindNetCoreRoot();
        var environment = File.ReadAllText(Path.Combine(root, "DevOps", ".env.example"));
        var compose = File.ReadAllText(Path.Combine(root, "DevOps", "docker-compose.override.yml"));
        var production = File.ReadAllText(Path.Combine(
            root,
            "DevOps",
            "environments",
            "production",
            "moderation.values.example.yaml"));

        Assert.Contains("MODERATION_DECISION_EXPORT_CURSOR_SIGNING_KEY=", environment, StringComparison.Ordinal);
        Assert.Contains("MODERATION_PRIVACY_SUBJECT_LOOKUP_KEY=", environment, StringComparison.Ordinal);
        Assert.Contains("DecisionExport__CursorSigningKey=${MODERATION_DECISION_EXPORT_CURSOR_SIGNING_KEY", compose, StringComparison.Ordinal);
        Assert.Contains("Moderation__Privacy__SubjectLookupKey=${MODERATION_PRIVACY_SUBJECT_LOOKUP_KEY", compose, StringComparison.Ordinal);
        Assert.Contains("moderation-api-runtime must expose DecisionExport__CursorSigningKey", production, StringComparison.Ordinal);
        Assert.Contains("Consumer/job Secrets also need the privacy key", production, StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static string FindNetCoreRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "DevOps")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the net-core-apps source root.");
    }

    [Fact]
    public void Operational_logging_excludes_exception_messages_and_restricted_values()
    {
        var logger = new CapturingLogger();
        const string protectedValue = "Server=db;Password=top-secret;reporter=subject-9";

        ModerationSafeLogging.LogSlaWorkerFailure(logger, new InvalidOperationException(protectedValue));
        ModerationSafeLogging.LogRetentionWorkerFailure(logger, new InvalidOperationException(protectedValue));

        Assert.Equal(2, logger.Messages.Count);
        foreach (var line in logger.Messages)
        {
            Assert.Contains(nameof(InvalidOperationException), line, StringComparison.Ordinal);
            Assert.DoesNotContain(protectedValue, line, StringComparison.Ordinal);
            Assert.DoesNotContain("top-secret", line, StringComparison.Ordinal);
            Assert.DoesNotContain("subject-9", line, StringComparison.Ordinal);
        }
    }

    private static ClaimsPrincipal Principal(string role) =>
        new(new ClaimsIdentity(
        [
            new Claim("sub", "actor-1"),
            new Claim(ClaimTypes.Role, role),
        ], "unit-test"));

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
