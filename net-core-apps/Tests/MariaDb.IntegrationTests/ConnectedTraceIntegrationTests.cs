using System.Collections.Concurrent;
using System.Diagnostics;
using Inspacy.Data.MariaDb;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;

namespace MariaDb.IntegrationTests;

public sealed class ConnectedTraceIntegrationTests
{
    private const string SensitivePayload = "trace-secret-payload-must-not-leak";
    private static readonly ActivitySource ApplicationActivities =
        new("Inspacy.TraceAcceptance");

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Http_application_mariadb_outbox_delivery_and_consumer_share_one_safe_trace()
    {
        var configuredConnection = Environment.GetEnvironmentVariable("TEST_MARIADB_CONNECTION");
        if (string.IsNullOrWhiteSpace(configuredConnection))
        {
            Assert.Skip(
                "Set TEST_MARIADB_CONNECTION to run the isolated MariaDB integration suite.");
        }

        var databaseName = $"connected_trace_{Guid.NewGuid():N}";
        Assert.StartsWith("connected_trace_", databaseName, StringComparison.Ordinal);
        var databaseConnection = WithDatabase(configuredConnection!, databaseName);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var databaseCreated = false;
        WebApplication? app = null;

        var activities = new ConcurrentQueue<ActivitySnapshot>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || source.Name.StartsWith("MassTransit", StringComparison.Ordinal)
                || source.Name is "Inspacy.Platform" or "Inspacy.TraceAcceptance",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(new ActivitySnapshot(
                activity.Source.Name,
                activity.OperationName,
                activity.Kind,
                activity.TraceId,
                activity.ParentSpanId,
                activity.TagObjects.ToDictionary(
                    tag => tag.Key,
                    tag => tag.Value,
                    StringComparer.Ordinal))),
        };
        ActivitySource.AddActivityListener(listener);

        try
        {
            await ExecuteAdministrativeCommand(
                configuredConnection!,
                $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;",
                timeout.Token);
            databaseCreated = true;

            var probe = new TraceProbe();
            var interceptor = new MariaDbCommandTelemetryInterceptor(
                new MariaDbCommandTelemetryOptions(5_000));
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(ConnectedTraceIntegrationTests).Assembly.GetName().Name,
                EnvironmentName = "Testing",
            });
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(probe);
            builder.Services.AddDbContext<TraceOutboxDbContext>(options => options
                .UseMySql(
                    databaseConnection,
                    new MariaDbServerVersion(new Version(10, 6, 0)),
                    provider => provider.CommandTimeout(10))
                .AddInterceptors(interceptor));
            builder.Services.AddMassTransit(registration =>
            {
                registration.AddConsumer<TraceProbeConsumer>();
                registration.AddEntityFrameworkOutbox<TraceOutboxDbContext>(outbox =>
                {
                    outbox.UseMySql();
                    outbox.UseBusOutbox();
                    outbox.QueryDelay = TimeSpan.FromMilliseconds(250);
                });
                registration.UsingInMemory((context, bus) =>
                {
                    bus.UseInstrumentation(serviceName: "Inspacy.ConnectedTraceAcceptance");
                    bus.ConfigureEndpoints(context);
                });
            });

            app = builder.Build();
            app.MapPost("/trace-acceptance", async (
                TraceOutboxDbContext db,
                IPublishEndpoint publisher,
                TraceProbe traceProbe,
                CancellationToken cancellationToken) =>
            {
                traceProbe.HttpContext = Activity.Current?.Context
                    ?? throw new InvalidOperationException("The HTTP server Activity is missing.");
                using var application = ApplicationActivities.StartActivity(
                    "application.command",
                    ActivityKind.Internal);
                traceProbe.ApplicationContext = application?.Context
                    ?? throw new InvalidOperationException("The application Activity is missing.");

                var intentId = Guid.NewGuid();
                await using var transaction = await db.Database.BeginTransactionAsync(
                    cancellationToken);
                db.Intents.Add(new TraceIntent(intentId, DateTimeOffset.UtcNow));
                await publisher.Publish(
                    new TraceProbeMessage(intentId, SensitivePayload),
                    send =>
                    {
                        send.MessageId = intentId;
                        send.CorrelationId = intentId;
                    },
                    cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                traceProbe.OutboxPersistedBeforeResponse = await db.Set<OutboxMessage>()
                    .AsNoTracking()
                    .AnyAsync(message => message.MessageId == intentId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Results.Accepted();
            });

            await using (var setupScope = app.Services.CreateAsyncScope())
            {
                var db = setupScope.ServiceProvider.GetRequiredService<TraceOutboxDbContext>();
                await db.Database.EnsureCreatedAsync(timeout.Token);
            }
            await app.StartAsync(timeout.Token);
            var addresses = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses
                ?? throw new InvalidOperationException("Kestrel did not expose a bound address.");
            var address = Assert.Single(addresses);

            using var client = new HttpClient { BaseAddress = new Uri(address) };
            using var response = await client.PostAsync(
                "/trace-acceptance",
                content: null,
                timeout.Token);
            Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
            await probe.Consumed.Task.WaitAsync(TimeSpan.FromSeconds(30), timeout.Token);

            Assert.True(probe.OutboxPersistedBeforeResponse);
            Assert.NotEqual(default, probe.HttpContext.TraceId);
            Assert.Equal(probe.HttpContext.TraceId, probe.ApplicationContext.TraceId);
            Assert.Equal(probe.HttpContext.TraceId, probe.ConsumerContext.TraceId);
            Assert.NotEqual(probe.HttpContext.SpanId, probe.ApplicationContext.SpanId);
            Assert.NotEqual(probe.ApplicationContext.SpanId, probe.ConsumerContext.SpanId);

            var connected = activities
                .Where(activity => activity.TraceId == probe.HttpContext.TraceId)
                .ToArray();
            Assert.Contains(connected, activity =>
                activity.Source.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                && activity.Kind == ActivityKind.Server);
            Assert.Contains(connected, activity =>
                activity.Source == "Inspacy.TraceAcceptance"
                && activity.Operation == "application.command");
            Assert.Contains(connected, activity =>
                activity.Source == MariaDbCommandTelemetryInterceptor.ActivitySourceName
                && activity.Operation == MariaDbCommandTelemetryInterceptor.ActivityName);
            Assert.Contains(connected, activity =>
                activity.Source.StartsWith("MassTransit", StringComparison.Ordinal)
                && activity.Kind is ActivityKind.Producer or ActivityKind.Internal);
            Assert.Contains(connected, activity =>
                activity.Source.StartsWith("MassTransit", StringComparison.Ordinal)
                && activity.Kind == ActivityKind.Consumer);

            var telemetryText = string.Join(
                '\n',
                connected.SelectMany(activity => activity.Tags)
                    .Select(tag => $"{tag.Key}={tag.Value}"));
            Assert.DoesNotContain(SensitivePayload, telemetryText, StringComparison.Ordinal);
            Assert.DoesNotContain(databaseName, telemetryText, StringComparison.Ordinal);
        }
        finally
        {
            if (app is not null)
            {
                using var stopBudget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await app.StopAsync(stopBudget.Token);
                await app.DisposeAsync();
            }
            if (databaseCreated)
            {
                using var cleanupBudget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await ExecuteAdministrativeCommand(
                    configuredConnection!,
                    $"DROP DATABASE `{databaseName}`;",
                    cleanupBudget.Token);
            }
        }
    }

    private static string WithDatabase(string connectionString, string databaseName)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            Database = databaseName,
        };
        return builder.ConnectionString;
    }

    private static async Task ExecuteAdministrativeCommand(
        string connectionString,
        string commandText,
        CancellationToken cancellationToken)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            Database = string.Empty,
        };
        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText, connection)
        {
            CommandTimeout = 10,
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record ActivitySnapshot(
        string Source,
        string Operation,
        ActivityKind Kind,
        ActivityTraceId TraceId,
        ActivitySpanId ParentSpanId,
        IReadOnlyDictionary<string, object?> Tags);

    public sealed record TraceProbeMessage(Guid IntentId, string SensitivePayload);

    public sealed class TraceProbe
    {
        public ActivityContext HttpContext { get; set; }
        public ActivityContext ApplicationContext { get; set; }
        public ActivityContext ConsumerContext { get; set; }
        public bool OutboxPersistedBeforeResponse { get; set; }
        public TaskCompletionSource Consumed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class TraceProbeConsumer(TraceProbe probe) : IConsumer<TraceProbeMessage>
    {
        public Task Consume(ConsumeContext<TraceProbeMessage> context)
        {
            probe.ConsumerContext = Activity.Current?.Context
                ?? throw new InvalidOperationException("The consumer Activity is missing.");
            probe.Consumed.TrySetResult();
            return Task.CompletedTask;
        }
    }

    public sealed class TraceIntent(Guid id, DateTimeOffset createdAtUtc)
    {
        private TraceIntent() : this(Guid.Empty, default) { }
        public Guid Id { get; private set; } = id;
        public DateTimeOffset CreatedAtUtc { get; private set; } = createdAtUtc;
    }

    public sealed class TraceOutboxDbContext(DbContextOptions<TraceOutboxDbContext> options)
        : DbContext(options)
    {
        public DbSet<TraceIntent> Intents => Set<TraceIntent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TraceIntent>(builder =>
            {
                builder.ToTable("TraceIntent");
                builder.HasKey(intent => intent.Id);
            });
            modelBuilder.AddInboxStateEntity();
            modelBuilder.AddOutboxMessageEntity();
            modelBuilder.AddOutboxStateEntity();
        }
    }
}
