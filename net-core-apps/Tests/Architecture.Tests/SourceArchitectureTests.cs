using System.Text.RegularExpressions;
using System.Text.Json;
using Xunit;

namespace Architecture.Tests;

public sealed class SourceArchitectureTests
{
    [Fact]
    public void Production_source_contains_no_security_or_sensitive_logging_bypass()
    {
        var sourceFiles = SourceFiles().ToArray();
        Assert.NotEmpty(sourceFiles);

        var violations = sourceFiles
            .SelectMany(path => ForbiddenPatterns
                .Where(pattern => Regex.IsMatch(File.ReadAllText(path), pattern.Expression))
                .Select(pattern => $"{Path.GetRelativePath(RepositoryRoot, path)}: {pattern.Name}"))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Forbidden production security bypasses were found:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Centrally_managed_projects_do_not_pin_package_versions_locally()
    {
        var versionPattern = new Regex(
            @"<PackageReference\b[^>]*\bVersion\s*=",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var violations = Directory.EnumerateFiles(RepositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .Where(path => versionPattern.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Package versions must be pinned in Directory.Packages.props:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Every_project_has_a_committed_dependency_lock_file()
    {
        var missing = Directory
            .EnumerateFiles(RepositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .Where(project => !File.Exists(Path.Combine(
                Path.GetDirectoryName(project)!,
                "packages.lock.json")))
            .Select(project => Path.GetRelativePath(RepositoryRoot, project))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "Every project must have packages.lock.json:" + Environment.NewLine +
            string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void Dotnet_container_builds_restore_only_from_lock_files()
    {
        var violations = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "DevOps", "Dockerfiles"), "*.Dockerfile")
            .Where(path => File.ReadAllText(path).Contains("dotnet restore", StringComparison.Ordinal))
            .Where(path => Regex.Matches(File.ReadAllText(path), @"dotnet restore[^\r\n]*")
                .Cast<Match>()
                .Any(match => !match.Value.Contains("--locked-mode", StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Container restores must use --locked-mode:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Application_source_contains_no_raw_RabbitMq_client_or_custom_broker_implementation()
    {
        var removedProject = Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "Brokers",
            "MessageBrokerRabbitMq",
            "MessageBrokerRabbitMq.csproj");
        Assert.False(File.Exists(removedProject), "The application-owned raw RabbitMQ project must stay removed.");

        var candidateFiles = Directory
            .EnumerateFiles(RepositoryRoot, "*", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .Where(path => !path.EndsWith(
                Path.Combine("Tests", "Architecture.Tests", "SourceArchitectureTests.cs"),
                StringComparison.OrdinalIgnoreCase))
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase));

        var violations = candidateFiles
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("MessageBrokerRabbitMq", StringComparison.Ordinal)
                    || source.Contains("PackageReference Include=\"RabbitMQ.Client\"", StringComparison.Ordinal)
                    || source.Contains("using RabbitMQ.Client", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Raw RabbitMQ application dependencies are forbidden; MassTransit owns the transport:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Every_service_transport_bridge_is_fail_closed_and_enlisted_in_its_transactional_outbox()
    {
        var servicesRoot = Path.Combine(RepositoryRoot, "Services");
        var actual = Directory.EnumerateFiles(servicesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .Where(path => Path.GetRelativePath(servicesRoot, path)
                .Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .All(segment => !segment.Equals("Tests", StringComparison.OrdinalIgnoreCase)
                    && !segment.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
                    && !segment.Equals("Migrations", StringComparison.OrdinalIgnoreCase)))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("IIntegrationMessagePublisher", StringComparison.Ordinal)
                    || source.Contains("IPublishEndpoint", StringComparison.Ordinal)
                    || source.Contains(".Broker.", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(actual);
        foreach (var path in actual)
        {
            var contextType = path.Split('/')[1] switch
            {
                "Comment" => "CommentContext",
                "Feed" => "FeedContext",
                "Following" => "FollowingContext",
                "Interaction" => "InteractionDbContext",
                "Moderation" => "ModerationDbContext",
                var service => throw new InvalidOperationException(
                    $"No transactional-outbox context mapping exists for service '{service}'."),
            };
            var source = File.ReadAllText(Path.Combine(
                RepositoryRoot,
                path.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains(contextType, source, StringComparison.Ordinal);
            Assert.Contains("PublishToBusOutboxAsync", source, StringComparison.Ordinal);
        }

        var enlistmentSource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "Brokers",
            "MessageBrokerExtensions",
            "EntityFrameworkBusOutboxExtensions.cs"));
        Assert.Contains("EnsureBusOutboxMessageStaged", enlistmentSource, StringComparison.Ordinal);
        Assert.Contains("pipe => pipe.MessageId =", enlistmentSource, StringComparison.Ordinal);
        Assert.Contains("stagedCount != 1", enlistmentSource, StringComparison.Ordinal);

        foreach (var service in new[] { "Comment", "Feed", "Following", "Interaction", "Moderation" })
        {
            var contextPath = service switch
            {
                "Moderation" => Path.Combine(
                    servicesRoot,
                    service,
                    $"{service}.Infraestructure",
                    "Database",
                    "Ef",
                    "ModerationDbContext.cs"),
                _ => Path.Combine(
                    servicesRoot,
                    service,
                    $"{service}.Infraestructure",
                    "Database",
                    "Ef",
                    "Context.cs"),
            };
            var contextSource = File.ReadAllText(contextPath);
            Assert.Contains("AddInboxStateEntity()", contextSource, StringComparison.Ordinal);
            Assert.Contains("AddOutboxMessageEntity()", contextSource, StringComparison.Ordinal);
            Assert.Contains("AddOutboxStateEntity()", contextSource, StringComparison.Ordinal);
        }

        var brokerSource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "Brokers",
            "MessageBrokerExtensions",
            "MassTransitBrokerExtensions.cs"));
        Assert.Contains("outbox.UseBusOutbox()", brokerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void MassTransit_traces_and_metrics_are_enabled_and_exported()
    {
        var brokerSource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "Brokers",
            "MessageBrokerExtensions",
            "MassTransitBrokerExtensions.cs"));
        var observabilitySource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "ApiKit",
            "CommomStartupExtensions",
            "ObservabilityExtensions.cs"));

        Assert.Contains("bus.UseInstrumentation(", brokerSource, StringComparison.Ordinal);
        Assert.Contains("AddOutboxBacklogMonitor(services, contextType)", brokerSource, StringComparison.Ordinal);
        Assert.Contains(".AddSource(\"MassTransit\")", observabilitySource, StringComparison.Ordinal);
        Assert.Contains(".AddMeter(\"MassTransit\")", observabilitySource, StringComparison.Ordinal);

        var outboxMonitorSource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "Brokers",
            "MessageBrokerExtensions",
            "MassTransitOutboxBacklogMonitor.cs"));
        Assert.Contains("inspacy.messaging.outbox.pending", outboxMonitorSource, StringComparison.Ordinal);
        Assert.Contains("inspacy.messaging.outbox.oldest_age", outboxMonitorSource, StringComparison.Ordinal);
        Assert.Contains("inspacy.messaging.outbox.sample_success", outboxMonitorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Body", outboxMonitorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DestinationAddress", outboxMonitorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageId", outboxMonitorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("exception.Message", outboxMonitorSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MassTransit_outbox_readiness_is_bounded_enabled_and_memory_only()
    {
        var expectedPendingBounds = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Comment"] = 100_000,
            ["Feed"] = 250_000,
            ["Following"] = 60_000,
            ["Interaction"] = 300_000,
            ["Moderation"] = 10_000,
        };

        foreach (var (service, expectedPendingBound) in expectedPendingBounds)
        {
            var path = Path.Combine(
                RepositoryRoot,
                "Services",
                service,
                $"{service}.Api",
                "appsettings.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var options = document.RootElement
                .GetProperty("Broker")
                .GetProperty("OutboxTelemetry");

            Assert.True(options.GetProperty("Enabled").GetBoolean());
            Assert.Equal(15, options.GetProperty("IntervalSeconds").GetInt32());
            Assert.Equal(5, options.GetProperty("QueryTimeoutSeconds").GetInt32());
            Assert.Equal(expectedPendingBound, options.GetProperty("MaximumPendingMessages").GetInt32());
            Assert.Equal(300, options.GetProperty("MaximumOldestMessageAgeSeconds").GetInt32());
            Assert.Equal(45, options.GetProperty("MaximumSampleAgeSeconds").GetInt32());
        }

        var brokerSource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "Brokers",
            "MessageBrokerExtensions",
            "MassTransitBrokerExtensions.cs"));
        var monitorSource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "Brokers",
            "MessageBrokerExtensions",
            "MassTransitOutboxBacklogMonitor.cs"));
        var healthSource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "Brokers",
            "MessageBrokerExtensions",
            "MassTransitOutboxBacklogHealthCheck.cs"));

        Assert.Contains("options => outboxContexts.Length == 0 || options.OutboxTelemetry.Enabled", brokerSource, StringComparison.Ordinal);
        Assert.Contains("tags: [\"ready\"]", brokerSource, StringComparison.Ordinal);
        Assert.Contains("FORCE INDEX (`PRIMARY`)", monitorSource, StringComparison.Ordinal);
        Assert.Contains("LIMIT @sampleLimit", monitorSource, StringComparison.Ordinal);
        Assert.Contains("MaximumPendingMessages + 1", monitorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DbConnection", healthSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateScope", healthSource, StringComparison.Ordinal);
    }

    [Fact]
    public void MariaDb_command_telemetry_is_safe_bounded_and_registered_everywhere()
    {
        var telemetryPath = Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "SqlKit",
            "MariaDbConfiguration",
            "MariaDbCommandTelemetryInterceptor.cs");
        var telemetrySource = File.ReadAllText(telemetryPath);

        Assert.Contains(": DbCommandInterceptor", telemetrySource, StringComparison.Ordinal);
        Assert.Contains("ActivitySourceName = PlatformInstrumentation.ActivitySourceName", telemetrySource, StringComparison.Ordinal);
        Assert.Contains("MeterName = PlatformInstrumentation.MeterName", telemetrySource, StringComparison.Ordinal);
        Assert.Contains("MariaDbCommandOutcome.Canceled", telemetrySource, StringComparison.Ordinal);
        Assert.Contains("MariaDbCommandOutcome.Failure", telemetrySource, StringComparison.Ordinal);
        Assert.Equal(
            2,
            Regex.Matches(telemetrySource, @"eventData\.Exception", RegexOptions.CultureInvariant).Count);
        Assert.Contains("ClassifyFailure(exception)", telemetrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("SetTag(\"exception", telemetrySource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception.Message", telemetrySource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RecordException", telemetrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("ILogger", telemetrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("db.statement", telemetrySource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("db.namespace", telemetrySource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server.address", telemetrySource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("command.Parameters", telemetrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("command.Connection", telemetrySource, StringComparison.Ordinal);

        foreach (var service in new[] { "Comment", "Feed", "Following", "Interaction", "Moderation" })
        {
            var extensionPath = Path.Combine(
                RepositoryRoot,
                "Services",
                service,
                $"{service}.Infraestructure",
                "Database",
                "Extension.cs");
            var extensionSource = File.ReadAllText(extensionPath);
            Assert.Contains(
                "services.AddMariaDbCommandTelemetry(configuration);",
                extensionSource,
                StringComparison.Ordinal);
            Assert.Contains(
                "options.UseMariaDbRuntime(connection, provider);",
                extensionSource,
                StringComparison.Ordinal);

            var appSettingsPath = Path.Combine(
                RepositoryRoot,
                "Services",
                service,
                $"{service}.Api",
                "appsettings.json");
            using var appSettings = JsonDocument.Parse(File.ReadAllText(appSettingsPath));
            Assert.Equal(
                250,
                appSettings.RootElement.GetProperty("Database")
                    .GetProperty("SlowQueryThresholdMilliseconds")
                    .GetInt32());
        }

        var providerPolicySource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "SqlKit",
            "MariaDbConfiguration",
            "MariaDbDbContextOptions.cs"));
        Assert.Contains(
            "options.AddMariaDbCommandTelemetry(serviceProvider);",
            providerPolicySource,
            StringComparison.Ordinal);

        var productionBase = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "DevOps",
            "environments",
            "production",
            "base.values.example.yaml"));
        Assert.Contains(
            "Database__SlowQueryThresholdMilliseconds: \"250\"",
            productionBase,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Authenticated_write_limiting_is_bounded_and_runs_after_authentication()
    {
        var hostSource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "ApiKit",
            "ApiHost",
            "InspacyApiHostExtensions.cs"));
        var trafficSource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "ApiKit",
            "CommomStartupExtensions",
            "TrafficProtectionExtensions.cs"));
        var commentController = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "Services",
            "Comment",
            "Comment.Api",
            "AppInterfaces",
            "Routes",
            "CommentsController.cs"));
        var authentication = hostSource.IndexOf(".UseAuthentication()", StringComparison.Ordinal);
        var rateLimiting = hostSource.IndexOf(".UseRateLimiter()", StringComparison.Ordinal);
        var authorization = hostSource.IndexOf(".UseAuthorization()", StringComparison.Ordinal);
        Assert.True(authentication >= 0 && authentication < rateLimiting && rateLimiting < authorization);
        Assert.Contains("AuthenticatedWritePartitionCount", trafficSource, StringComparison.Ordinal);
        Assert.Contains("hash % (uint)settings.AuthenticatedWritePartitionCount", trafficSource, StringComparison.Ordinal);
        Assert.Contains("AnonymousViewPartitionCount", trafficSource, StringComparison.Ordinal);
        Assert.Contains("hash % (uint)settings.AnonymousViewPartitionCount", trafficSource, StringComparison.Ordinal);
        Assert.Contains(
            "EnableRateLimiting(TrafficProtectionPolicies.AuthenticatedWrite)",
            commentController,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Feed_routes_declare_applicable_problem_contracts()
    {
        var feedRoutes = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "Services",
            "Feed",
            "Feed.Api",
            "AppInterfaces",
            "Routes",
            "Feed.route.cs"));
        AssertRouteProblemContracts(
            feedRoutes,
            "group.MapGet(\"/user/auth/items\"",
            authenticated: true,
            validatesInput: true);
        AssertRouteProblemContracts(
            feedRoutes,
            "group.MapPost(\"/user/auth/seen\"",
            authenticated: true,
            validatesInput: true);
    }

    [Fact]
    public void Http_availability_uses_exact_bounded_outcomes_and_wraps_final_response_handling()
    {
        var hostSource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "ApiKit",
            "ApiHost",
            "InspacyApiHostExtensions.cs"));
        var telemetrySource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "ApiKit",
            "CommomStartupExtensions",
            "ObservabilityExtensions.cs"));
        var trafficSource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "ApiKit",
            "CommomStartupExtensions",
            "TrafficProtectionExtensions.cs"));
        var exceptionSource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "ApiKit",
            "ApiExceptionHandler",
            "ApiProblemDetailsExtensions.cs"));
        var recordingRules = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "DevOps",
            "observability",
            "prometheus",
            "recording-rules.yaml"));

        var pipelineStart = hostSource.IndexOf(
            "private WebApplication BuildApplication()",
            StringComparison.Ordinal);
        Assert.True(pipelineStart >= 0);
        var pipelineSource = hostSource[pipelineStart..];
        var requestLogging = pipelineSource.IndexOf(
            "UseMiddleware<RequestLoggingMiddleware>()",
            StringComparison.Ordinal);
        var exceptionHandling = pipelineSource.IndexOf("UseExceptionHandler(", StringComparison.Ordinal);
        var requestTimeouts = pipelineSource.IndexOf("UseRequestTimeouts()", StringComparison.Ordinal);
        Assert.True(
            requestLogging >= 0 && requestLogging < exceptionHandling && requestLogging < requestTimeouts,
            "Request outcome telemetry must wrap exception and timeout handling to observe the final response.");

        Assert.Contains("inspacy.http.server.request.outcomes", telemetrySource, StringComparison.Ordinal);
        Assert.Contains("http_request_outcome", telemetrySource, StringComparison.Ordinal);
        Assert.Contains("RoutePattern.RawText", telemetrySource, StringComparison.Ordinal);
        Assert.Contains("OutcomeTaggedRateLimiter", trafficSource, StringComparison.Ordinal);
        Assert.Contains("HttpRequestOutcome.LoadShed", trafficSource, StringComparison.Ordinal);
        Assert.Contains("Status503ServiceUnavailable", trafficSource, StringComparison.Ordinal);
        Assert.Contains("HttpRequestOutcome.ClientRateLimited", trafficSource, StringComparison.Ordinal);
        Assert.Contains("HttpRequestOutcome.DependencyUnavailable", exceptionSource, StringComparison.Ordinal);
        Assert.Contains("HttpRequestOutcome.ServerTimeout", exceptionSource, StringComparison.Ordinal);
        Assert.Contains("inspacy_http_server_request_outcomes_total", recordingRules, StringComparison.Ordinal);
        Assert.Contains(
            "load_shed|server_timeout|dependency_unavailable|server_error",
            recordingRules,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "http_response_status_code=~\"429|5..\"",
            recordingRules,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_role_is_selected_before_secrets_and_every_service_alias_is_role_scoped()
    {
        var hostSource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "ApiKit",
            "ApiHost",
            "InspacyApiHostExtensions.cs"));
        var roleSelection = hostSource.IndexOf(
            "ResolveRuntimeRole(builder.Configuration)",
            StringComparison.Ordinal);
        var roleValidation = hostSource.IndexOf(
            "ValidateRuntimeRole(configuredRole, builder.Environment)",
            StringComparison.Ordinal);
        var secretResolution = hostSource.IndexOf(
            "builder.Configuration.AddSecretApiProvider(",
            StringComparison.Ordinal);

        Assert.True(roleSelection >= 0 && roleSelection < roleValidation);
        Assert.True(roleValidation < secretResolution);
        Assert.Contains(
            "builder.Environment.EnvironmentName",
            hostSource[secretResolution..],
            StringComparison.Ordinal);

        var appSettings = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "Services"), "appsettings.json", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .Where(path => path.Contains(
                $"{Path.DirectorySeparatorChar}Services{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase));
        foreach (var path in appSettings)
        {
            var source = File.ReadAllText(path);
            using var document = JsonDocument.Parse(source);
            if (!document.RootElement.TryGetProperty("SecretProvider", out var secretProvider)
                || !secretProvider.TryGetProperty("References", out var references))
            {
                continue;
            }

            foreach (var reference in references.EnumerateObject())
            {
                Assert.Contains(
                    $"{{{{secret:{reference.Name}}}}}",
                    source,
                    StringComparison.Ordinal);
                Assert.True(
                    reference.Value.TryGetProperty("Roles", out var rolesElement),
                    $"{Path.GetRelativePath(RepositoryRoot, path)} alias {reference.Name} has no Roles allow-list.");
                var roles = rolesElement.EnumerateArray()
                    .Select(role => role.GetString())
                    .Where(role => role is not null)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                Assert.NotEmpty(roles);
                if (reference.Name.Contains("Identity", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.DoesNotContain("ConsumerWorker", roles);
                    Assert.DoesNotContain("JobWorker", roles);
                    Assert.DoesNotContain("Migration", roles);
                }
                if (reference.Name.Contains("Broker", StringComparison.OrdinalIgnoreCase))
                    Assert.DoesNotContain("Migration", roles);
                if (reference.Name.Contains("Database", StringComparison.OrdinalIgnoreCase))
                    Assert.Contains("Migration", roles);
            }
        }
    }

    [Fact]
    public void Service_layers_use_aggregates_features_and_concrete_jobs_without_contract_projects()
    {
        var servicesRoot = Path.Combine(RepositoryRoot, "Services");
        var forbiddenProjects = Directory
            .EnumerateFiles(servicesRoot, "*.Contracts.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToArray();
        Assert.True(
            forbiddenProjects.Length == 0,
            "Service-level Contracts projects are forbidden; integration messages belong in the shared broker boundary:" +
            Environment.NewLine + string.Join(Environment.NewLine, forbiddenProjects));

        var forbiddenContractDirectories = Directory
            .EnumerateDirectories(servicesRoot, "*.Contracts", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToArray();
        Assert.True(
            forbiddenContractDirectories.Length == 0,
            "Service-level Contracts directories are forbidden; integration messages belong in the shared broker boundary:" +
            Environment.NewLine + string.Join(Environment.NewLine, forbiddenContractDirectories));

        var jobExtensions = Directory
            .EnumerateFiles(servicesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains(
                $"{Path.DirectorySeparatorChar}AppInterfaces{Path.DirectorySeparatorChar}Jobs{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => Path.GetFileName(path).Equals("JobsExtensions.cs", StringComparison.OrdinalIgnoreCase)
                || File.ReadAllText(path).Contains("ConfigureJobs(", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToArray();
        Assert.True(
            jobExtensions.Length == 0,
            "AppInterfaces jobs are concrete BackgroundService types and must not use JobsExtensions:" +
            Environment.NewLine + string.Join(Environment.NewLine, jobExtensions));

        foreach (var service in new[] { "Interaction", "Moderation" })
        {
            var serviceRoot = Path.Combine(servicesRoot, service);
            var domainRoot = Path.Combine(serviceRoot, $"{service}.Domain");
            var aggregatesRoot = Path.Combine(domainRoot, "AggregatesModel");
            var applicationRoot = Path.Combine(serviceRoot, $"{service}.Application");
            var featuresRoot = Path.Combine(applicationRoot, "Features");
            var infrastructureRoot = Path.Combine(serviceRoot, $"{service}.Infraestructure");

            Assert.True(Directory.Exists(aggregatesRoot));
            Assert.NotEmpty(Directory.EnumerateFiles(aggregatesRoot, "*.cs", SearchOption.AllDirectories));
            Assert.True(Directory.Exists(Path.Combine(domainRoot, "Repositories")));
            Assert.True(Directory.Exists(Path.Combine(applicationRoot, "Dtos")));
            Assert.True(Directory.Exists(featuresRoot));
            Assert.Contains(
                Directory.EnumerateFiles(featuresRoot, "*.cs", SearchOption.AllDirectories),
                path => path.Contains(
                    $"{Path.DirectorySeparatorChar}Commands{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                Directory.EnumerateFiles(featuresRoot, "*.cs", SearchOption.AllDirectories),
                path => path.Contains(
                    $"{Path.DirectorySeparatorChar}Queries{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase));
            Assert.True(Directory.Exists(Path.Combine(infrastructureRoot, "Database", "Ef")));
            Assert.True(Directory.Exists(Path.Combine(infrastructureRoot, "Providers")));
            Assert.False(Directory.Exists(Path.Combine(serviceRoot, $"{service}.Domain", "Model")));
            Assert.False(Directory.Exists(Path.Combine(serviceRoot, $"{service}.Infraestructure", "Persistence")));
        }

        Assert.False(Directory.Exists(Path.Combine(
            servicesRoot,
            "Comment",
            "Comment.Infraestructure",
            "InteractionCutover")));
    }

    [Fact]
    public void Production_async_interface_boundaries_accept_cancellation()
    {
        var asyncMethod = new Regex(
            @"^\s*(?:Task|ValueTask|IAsyncEnumerable)(?:<[^\r\n]+>)?\s+(?<method>\w+)\s*\((?<parameters>.*?)\)\s*(?:where[^;]+)?;",
            RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        var candidates = new[] { "Services", "BuildingBlocks" }
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, root),
                "*.cs",
                SearchOption.AllDirectories))
            .Where(IsSourcePath)
            .Where(path => Path.GetRelativePath(RepositoryRoot, path)
                .Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .All(segment => !segment.Equals("Tests", StringComparison.OrdinalIgnoreCase)
                    && !segment.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
                    && !segment.Equals("Migrations", StringComparison.OrdinalIgnoreCase)))
            .Select(path => (Path: path, Source: File.ReadAllText(path)))
            .Where(file => Regex.IsMatch(
                file.Source,
                @"\binterface\b",
                RegexOptions.CultureInvariant));

        var violations = candidates
            .SelectMany(file => asyncMethod.Matches(file.Source)
                .Cast<Match>()
                .Where(match => !Regex.IsMatch(
                    match.Groups["parameters"].Value,
                    @"\bCancellationToken\b",
                    RegexOptions.CultureInvariant))
                .Select(match =>
                    $"{Path.GetRelativePath(RepositoryRoot, file.Path)}: {match.Groups["method"].Value}"))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Production async interface boundaries must accept a caller cancellation token:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Migration_paths_are_cancellation_aware_and_have_no_sync_exceptions()
    {
        var sqlKitRoot = Path.Combine(RepositoryRoot, "BuildingBlocks", "SqlKit");
        var guardedProjects = new[]
        {
            Path.Combine(sqlKitRoot, "SqlMigrations", "SqlMigrations.csproj"),
            Path.Combine(sqlKitRoot, "EfCoreMigrations", "EfCoreMigrations.csproj"),
        };

        foreach (var project in guardedProjects)
        {
            Assert.Contains(
                "<WarningsAsErrors>$(WarningsAsErrors);CA2016</WarningsAsErrors>",
                File.ReadAllText(project),
                StringComparison.Ordinal);
        }
        Assert.Contains(
            "dotnet_diagnostic.CA2016.severity = error",
            File.ReadAllText(Path.Combine(sqlKitRoot, ".editorconfig")),
            StringComparison.Ordinal);

        var apiHostPath = Path.Combine(
            RepositoryRoot,
            "BuildingBlocks",
            "ApiKit",
            "ApiHost",
            "InspacyApiHostExtensions.cs");
        var apiHostSource = File.ReadAllText(apiHostPath);
        Assert.DoesNotContain("MIGRATION_ASYNC_COMPATIBILITY", apiHostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult()", apiHostSource, StringComparison.Ordinal);

        var guardedSources = Directory
            .EnumerateFiles(sqlKitRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .Append(apiHostPath);
        var forbidden = new[]
        {
            (Name: "synchronous EF migration", Expression: @"\.Migrate\s*\("),
            (Name: "blocking sleep", Expression: @"\bThread\.Sleep\s*\("),
            (Name: "blocking task result", Expression: @"\.Result\b"),
            (Name: "blocking task wait", Expression: @"\.Wait\s*\("),
            (Name: "sync-over-async", Expression: @"\.GetAwaiter\s*\(\)\s*\.GetResult\s*\(\)"),
            (Name: "unbounded token source", Expression: @"CancellationToken\.None"),
        };

        var violations = guardedSources
            .SelectMany(path =>
            {
                return forbidden
                    .Where(pattern => Regex.IsMatch(
                        File.ReadAllText(path),
                        pattern.Expression,
                        RegexOptions.CultureInvariant))
                    .Select(pattern =>
                        $"{Path.GetRelativePath(RepositoryRoot, path)}: {pattern.Name}");
            })
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Migration and cleanup paths must remain async and cancellation-aware:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));

        var synchronousEntryPoints = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "Services"), "Program.cs", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .Where(path => Regex.IsMatch(
                File.ReadAllText(path),
                @"\bApiBuilder\.New\s*\(",
                RegexOptions.CultureInvariant))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.Empty(synchronousEntryPoints);
    }

    [Fact]
    public void Every_concrete_application_job_has_a_bounded_trace_and_logging_scope()
    {
        var jobs = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "Services"), "*Job.cs", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .Where(path => path.Replace('\\', '/').Contains(
                "/AppInterfaces/Jobs/",
                StringComparison.Ordinal))
            .ToArray();
        var violations = jobs
            .Where(path => !File.ReadAllText(path).Contains(
                "JobExecutionScope.Begin(",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToArray();

        Assert.NotEmpty(jobs);
        Assert.True(
            violations.Length == 0,
            "Every concrete application job iteration must use the shared bounded trace/log scope:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Service_owned_persistence_cache_and_operational_assets_use_canonical_locations()
    {
        var servicesRoot = Path.Combine(RepositoryRoot, "Services");
        var persistenceRole = new Regex(
            @"(?:Repository|Store|Reader|Writer|Reconciler)$",
            RegexOptions.CultureInvariant);
        var nonEfPersistencePorts = new HashSet<string>(StringComparer.Ordinal)
        {
            // External object storage is a provider boundary, not a relational EF adapter.
            "ICommentMediaObjectStore",
        };
        var servicePersistencePorts = Directory
            .EnumerateFiles(servicesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .Where(path => Path.GetRelativePath(servicesRoot, path)
                .Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment.EndsWith(".Application", StringComparison.OrdinalIgnoreCase)
                    || segment.EndsWith(".Domain", StringComparison.OrdinalIgnoreCase)))
            .SelectMany(path => Regex.Matches(
                    File.ReadAllText(path),
                    @"\binterface\s+(?<name>I[A-Z]\w*)\b",
                    RegexOptions.CultureInvariant)
                .Cast<Match>()
                .Select(match => match.Groups["name"].Value)
                .Where(port => persistenceRole.IsMatch(port)
                    && !nonEfPersistencePorts.Contains(port)))
            .ToHashSet(StringComparer.Ordinal);

        var concreteEfType = new Regex(
            @"(?ms)^\s*(?<modifiers>(?:(?:public|internal|private|protected|file|new|sealed|static|abstract|partial|unsafe|readonly|ref)\s+)*)(?:class|struct|record(?:\s+(?:class|struct))?)\s+(?<name>[A-Z]\w*)(?<declaration>[^{;]*?)(?:\r?\n\s*)?\{",
            RegexOptions.CultureInvariant);
        Assert.All(
            new[]
            {
                "sealed class ImplicitStore : IProjectionWriter { }",
                "file sealed class FileReader : IProjectionReader { }",
                "internal sealed record class RecordWriter : IProjectionWriter { }",
                "readonly struct ValueRepository : IValueRepository { }",
                "file readonly record struct ValueStore : IValueStore { }",
                "private sealed class NestedReader : IProjectionReader { }",
                "protected internal ref struct NestedWriter { }",
                "private protected readonly record struct NestedReconciler : IProjectionReconciler { }",
            },
            declaration => Assert.True(
                concreteEfType.IsMatch(declaration),
                $"Persistence adapter declaration form was not recognized: {declaration}"));

        var repositoryImplementations = Directory
            .EnumerateFiles(servicesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .Where(path => Path.GetRelativePath(servicesRoot, path)
                .Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .All(segment => !segment.Equals("Tests", StringComparison.OrdinalIgnoreCase)
                    && !segment.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
                    && !segment.Equals("Migrations", StringComparison.OrdinalIgnoreCase)))
            .SelectMany(path => concreteEfType.Matches(File.ReadAllText(path))
                .Cast<Match>()
                .Select(match =>
                {
                    var typeName = match.Groups["name"].Value;
                    var modifiers = match.Groups["modifiers"].Value;
                    var declaration = match.Groups["declaration"].Value;
                    var inheritanceSeparator = declaration.LastIndexOf(':');
                    var implementedPorts = inheritanceSeparator < 0
                        ? []
                        : Regex.Matches(
                                declaration[(inheritanceSeparator + 1)..],
                                @"\bI[A-Z]\w*\b",
                                RegexOptions.CultureInvariant)
                            .Cast<Match>()
                            .Select(port => port.Value)
                            .ToArray();
                    var isConcrete = !Regex.IsMatch(
                        modifiers,
                        @"\b(?:abstract|static)\b",
                        RegexOptions.CultureInvariant);
                    var hasExplicitPersistenceRole = persistenceRole.IsMatch(typeName);
                    var implementsServicePersistencePort = implementedPorts.Any(
                        servicePersistencePorts.Contains);

                    return (Path: path, TypeName: typeName, IsPersistenceAdapter:
                        isConcrete && (hasExplicitPersistenceRole || implementsServicePersistencePort));
                }))
            .Where(type => type.IsPersistenceAdapter)
            .ToArray();

        Assert.NotEmpty(repositoryImplementations);
        var misplacedRepositoryImplementations = repositoryImplementations
            .Where(type => !Path.GetRelativePath(RepositoryRoot, type.Path)
                .Replace('\\', '/')
                .Contains(
                    ".Infraestructure/Database/Ef/Repositories/",
                    StringComparison.Ordinal))
            .Select(type =>
                $"{Path.GetRelativePath(RepositoryRoot, type.Path)}: {type.TypeName}")
            .ToArray();
        Assert.True(
            misplacedRepositoryImplementations.Length == 0,
            "EF persistence-port implementations must live under Database/Ef/Repositories:" +
            Environment.NewLine + string.Join(Environment.NewLine, misplacedRepositoryImplementations));

        var cacheRegistrations = Directory
            .EnumerateFiles(servicesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .Where(path => File.ReadAllText(path).Contains(
                "AddInspacyHybridCache(",
                StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(cacheRegistrations);
        Assert.All(cacheRegistrations, path => Assert.Contains(
            ".Infraestructure/Database/Redis/",
            Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/'),
            StringComparison.Ordinal));

        var cacheProjectReferences = Directory
            .EnumerateFiles(servicesRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "BuildingBlocks\\Caching\\HybridCache",
                StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(cacheProjectReferences);
        Assert.All(cacheProjectReferences, path => Assert.Contains(
            ".Infraestructure/",
            Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/'),
            StringComparison.Ordinal));

        var followingEfRoot = Path.Combine(
            servicesRoot,
            "Following",
            "Following.Infraestructure",
            "Database",
            "Ef");
        Assert.Equal(
            ["Context.cs", "ContextFactory.cs"],
            Directory.EnumerateFiles(followingEfRoot, "*.cs", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal));

        Assert.False(Directory.Exists(Path.Combine(servicesRoot, "TestSupport", "Performance")));
        Assert.False(Directory.Exists(Path.Combine(RepositoryRoot, "deploy")));
        Assert.True(Directory.Exists(Path.Combine(RepositoryRoot, "Tests", "PerformanceHelpers")));
        Assert.True(Directory.Exists(Path.Combine(RepositoryRoot, "DevOps")));

        var retiredProjects = new[]
        {
            "BuildingBlocks/Providers/FeedProvider/FeedProvider.csproj",
            "BuildingBlocks/Providers/NotificationProvider/NotificationProvider.csproj",
            "BuildingBlocks/Providers/Posts.Provider/Posts.Provider.csproj",
            "BuildingBlocks/Providers/UserProvider/UserProvider.csproj",
            "BuildingBlocks/Brokers/MessageBrokerRabbitMq/MessageBrokerRabbitMq.csproj",
            "BuildingBlocks/OutBoxKit/OutBoxKit.csproj",
            "BuildingBlocks/ApiKit/PaginationHelper/PaginationHelper.csproj",
            "BuildingBlocks/SqlKit/EfCoreFilters/EfCoreFilters.csproj",
        };
        Assert.All(retiredProjects, path => Assert.False(File.Exists(Path.Combine(
            RepositoryRoot,
            path.Replace('/', Path.DirectorySeparatorChar))), path));

        var retiredRuntimeTerms = new Regex(
            @"(?:legacy|obsolete|obselete|deprecated|cutover|strangler)|" +
            @"\b(?:dual|shadow)[-_ ]*(?:read|write|path|projection)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var guardedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".json",
            ".csproj",
            ".props",
            ".targets",
            ".xml",
            ".yml",
            ".yaml",
        };
        var retiredRuntimeFiles = new[] { "Services", "BuildingBlocks" }
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, root),
                "*",
                SearchOption.AllDirectories))
            .Where(IsSourcePath)
            .Where(path => guardedExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsTestPath(path))
            .ToArray();
        Assert.NotEmpty(retiredRuntimeFiles);

        var retiredTermViolations = retiredRuntimeFiles
            .Where(path => retiredRuntimeTerms.IsMatch(
                    Path.GetRelativePath(RepositoryRoot, path))
                || retiredRuntimeTerms.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToArray();
        Assert.True(
            retiredTermViolations.Length == 0,
            "Production runtime source/configuration contains retired or transition implementation terminology:" +
            Environment.NewLine + string.Join(Environment.NewLine, retiredTermViolations));
    }

    [Fact]
    public void Forward_removal_migrations_do_not_recreate_removed_schema()
    {
        var removalMigrations = new[]
        {
            "Services/Comment/Comment.Infraestructure/Database/Ef/Migrations/20260718040856_RemoveRetiredCommentArtifacts.cs",
            "Services/Comment/Comment.Infraestructure/Database/Ef/Migrations/20260718120000_RemoveCommentCompatibilityArtifacts.cs",
            "Services/Feed/Feed.Infraestructure/Database/Ef/Migrations/20260723000000_RemoveRetiredFeedProjection.cs",
            "Services/Following/Following.Infraestructure/Database/Ef/Migrations/20260722000000_DropRetiredAnalyticsTable.cs",
            "Services/Interaction/Interaction.Infraestructure/Database/Ef/Migrations/20260722010000_RemoveRetiredCommentImportState.cs",
            "Services/Moderation/Moderation.Infraestructure/Database/Ef/Migrations/20260722011000_RemoveRetiredModerationSourceRequests.cs",
        };

        foreach (var relativePath in removalMigrations)
        {
            var source = File.ReadAllText(Path.Combine(
                RepositoryRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var downStart = source.IndexOf(
                "protected override void Down",
                StringComparison.Ordinal);
            Assert.True(downStart >= 0, $"Missing Down method: {relativePath}");

            var down = source[downStart..];
            Assert.Contains("throw new NotSupportedException", down, StringComparison.Ordinal);
            Assert.DoesNotContain("migrationBuilder.Create", down, StringComparison.Ordinal);
            Assert.DoesNotContain("migrationBuilder.Add", down, StringComparison.Ordinal);
        }
    }

    private static readonly (string Name, string Expression)[] ForbiddenPatterns =
    [
        ("disabled TLS validation", @"DangerousAcceptAnyServerCertificateValidator"),
        ("disabled TLS validation callback", @"ServerCertificate(?:Custom)?ValidationCallback\s*\+?=\s*[^;]*=>\s*true"),
        ("wildcard CORS", @"\.AllowAnyOrigin\s*\("),
        ("wildcard CORS callback", @"\.SetIsOriginAllowed\s*\(\s*[^)]*=>\s*true\s*\)"),
        ("EF Core sensitive-data logging", @"\.EnableSensitiveDataLogging\s*\("),
        ("raw request-body logging", @"Log(?:Information|Warning|Error|Debug|Trace)\s*\([^;]*(?:Request\.Body|payload|rawBody)"),
    ];

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(RepositoryRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .Where(path => !IsTestPath(path));

    private static bool IsTestPath(string path)
    {
        var relativePath = Path.GetRelativePath(RepositoryRoot, path);
        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return segments
            .Take(Math.Max(segments.Length - 1, 0))
            .Any(segment => segment.Equals("Tests", StringComparison.OrdinalIgnoreCase)
                || segment.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSourcePath(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static void AssertRouteProblemContracts(
        string source,
        string registrationMarker,
        bool authenticated,
        bool validatesInput)
    {
        var start = source.IndexOf(registrationMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing route registration '{registrationMarker}'.");
        var end = source.IndexOf(';', start);
        Assert.True(end > start, $"Unterminated route registration '{registrationMarker}'.");
        var registration = source[start..end];

        Assert.Contains(
            ".ProducesProblem(StatusCodes.Status503ServiceUnavailable)",
            registration,
            StringComparison.Ordinal);
        Assert.Contains(
            ".ProducesProblem(StatusCodes.Status500InternalServerError)",
            registration,
            StringComparison.Ordinal);

        if (validatesInput)
        {
            Assert.Contains(
                ".ProducesProblem(StatusCodes.Status400BadRequest)",
                registration,
                StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain(
                ".ProducesProblem(StatusCodes.Status400BadRequest)",
                registration,
                StringComparison.Ordinal);
        }

        foreach (var status in new[] { "Status401Unauthorized", "Status403Forbidden" })
        {
            if (authenticated)
                Assert.Contains(status, registration, StringComparison.Ordinal);
            else
                Assert.DoesNotContain(status, registration, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the net-core-apps repository root.");
    }
}
