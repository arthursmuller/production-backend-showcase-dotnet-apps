using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using ApiExceptionHandling;
using AuthUtils;
using Comment.API.AppInterfaces.Routes;
using Comment.Application.Dtos;
using Comment.Application.Features.Comments.Commands;
using Comment.Application.Features.Media.Commands;
using Comment.Application.Features.Privacy;
using Comment.Application.Providers.Storage;
using Comment.Domain.AggregatesModel;
using Comment.Domain.Repositories;
using Following.API.AppInterfaces.Routes;
using Following.Application.Dtos;
using Following.Application.Features.Followings;
using Following.Domain.AggregatesModel;
using Following.Domain.Repositories;
using IdentityProvider;
using Interaction.Api;
using Interaction.Api.AppInterfaces.Routes;
using CommomStartupExtensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moderation.API;
using Moderation.API.AppInterfaces.Routes;
using Moderation.Application.Features.Cases.Commands;
using Moderation.Application.Options;
using StarterEntities;
using Xunit;

namespace ApiContract.Tests;

public sealed class HttpEndpointContractTests
{
    private static readonly HashSet<string> OpenApiMethods = new(
        ["get", "post", "put", "patch", "delete", "head", "options", "trace"],
        StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task Following_create_and_delete_semantics_execute_over_http()
    {
        await using var host = await HttpContractHost.StartAsync();

        using var create = await host.SendJsonAsync(
            HttpMethod.Post,
            "/api/followings/friend/request",
            new RequestFriendshipDto { UserId = 20 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(
            "/api/followings/relationships/20",
            create.Headers.Location!.OriginalString);
        Assert.StartsWith("application/json", create.Content.Headers.ContentType!.ToString());

        using var delete = await host.SendAsync(
            HttpMethod.Delete,
            "/api/followings/friend/request/20");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(await delete.Content.ReadAsByteArrayAsync(
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Comment_create_is_201_with_a_resolvable_location_and_problem_details()
    {
        await using var host = await HttpContractHost.StartAsync();

        using var created = await host.SendMultipartCommentAsync("/api/comments");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Matches(
            "^/api/comments/[0-9]+$",
            created.Headers.Location!.AbsolutePath);
        Assert.StartsWith("application/json", created.Content.Headers.ContentType!.ToString());
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken));
        var createdData = createdBody.RootElement.GetProperty("data");
        Assert.Equal("post-42", createdData.GetProperty("targetId").GetString());
        Assert.Equal("post", createdData.GetProperty("targetType").GetString());
        Assert.False(createdData.TryGetProperty("engagement", out _));
        Assert.False(createdData.TryGetProperty("interactionStatus", out _));
        Assert.False(createdData.TryGetProperty("social", out _));
        Assert.False(createdData.TryGetProperty("title", out _));

        using var invalid = await host.SendInvalidMultipartCommentAsync("/api/comments");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.StartsWith(
            "application/problem+json",
            invalid.Content.Headers.ContentType!.ToString());
        using var problem = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken));
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
        Assert.True(problem.RootElement.TryGetProperty("correlationId", out _));
    }

    [Fact]
    public async Task Moderation_preconditions_return_canonical_problem_details_over_http()
    {
        await using var host = await HttpContractHost.StartAsync();
        var caseId = Guid.Parse("db687941-1563-4a2e-abed-5792ae90e9f7");

        using var response = await host.SendJsonAsync(
            HttpMethod.Post,
            $"/internal/moderation/triage/cases/{caseId:D}",
            new { priority = 2, reasonCode = "policy-review", expectedVersion = 0 });

        Assert.Equal((HttpStatusCode)428, response.StatusCode);
        Assert.StartsWith(
            "application/problem+json",
            response.Content.Headers.ContentType!.ToString());
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken));
        Assert.Equal("if-match-required", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(428, problem.RootElement.GetProperty("status").GetInt32());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
        Assert.True(problem.RootElement.TryGetProperty("correlationId", out _));
        Assert.DoesNotContain("stack", problem.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Generated_openapi_exposes_only_canonical_success_and_problem_response_contracts()
    {
        await using var host = await HttpContractHost.StartAsync();
        using var response = await host.Client.GetAsync(
            "/openapi/api.json",
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken));
        var paths = document.RootElement.GetProperty("paths");

        Assert.DoesNotContain(
            paths.EnumerateObject(),
            path => Regex.IsMatch(
                path.Name,
                @"/v[0-9]+(?:/|$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        Assert.DoesNotContain(
            paths.EnumerateObject(),
            path => path.Name.StartsWith(
                "/api/socialinteractions",
                StringComparison.OrdinalIgnoreCase));

        var following = paths
            .GetProperty("/api/followings/friend/request")
            .GetProperty("post");
        Assert.True(following.GetProperty("responses").TryGetProperty("201", out var followingCreated));
        Assert.True(followingCreated.GetProperty("content").TryGetProperty("application/json", out _));
        Assert.True(following.GetProperty("responses").GetProperty("409")
            .GetProperty("content").TryGetProperty("application/problem+json", out _));

        var feedContracts = new (string Path, string Method, string[] Statuses, bool Authenticated)[]
        {
            ("/api/feed/user/auth/items", "get",
                ["200", "400", "401", "403", "404", "503", "500"], true),
            ("/api/feed/user/auth/seen", "post",
                ["204", "400", "401", "403", "404", "503", "500"], true),
        };
        foreach (var contract in feedContracts)
        {
            var responses = paths.GetProperty(contract.Path)
                .GetProperty(contract.Method)
                .GetProperty("responses");
            foreach (var status in contract.Statuses)
            {
                Assert.True(
                    responses.TryGetProperty(status, out var documented),
                    $"{contract.Method.ToUpperInvariant()} {contract.Path} is missing {status}.");
                if (status is not ("200" or "204"))
                {
                    Assert.True(documented.GetProperty("content")
                        .TryGetProperty("application/problem+json", out _));
                }
            }
            if (!contract.Authenticated)
            {
                Assert.False(responses.TryGetProperty("401", out _));
                Assert.False(responses.TryGetProperty("403", out _));
            }
        }

        var comment = paths.GetProperty("/api/comments").GetProperty("post");
        Assert.True(comment.GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(comment.GetProperty("responses").GetProperty("400")
            .GetProperty("content").TryGetProperty("application/problem+json", out _));

        var moderation = paths
            .GetProperty("/api/moderation/reports")
            .GetProperty("post");
        Assert.True(moderation.GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(moderation.GetProperty("responses").GetProperty("409")
            .GetProperty("content").TryGetProperty("application/problem+json", out _));

        Assert.DoesNotContain(
            paths.EnumerateObject()
                .SelectMany(path => path.Value.EnumerateObject())
                .Where(operation => OpenApiMethods.Contains(operation.Name)),
            operation => operation.Value.TryGetProperty("deprecated", out var value)
                && value.ValueKind == JsonValueKind.True);

        var routeOperations = ((IEndpointRouteBuilder)host.App).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
                (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => $"{method}:{endpoint.RoutePattern.RawText}"))
            .ToArray();
        Assert.Equal(routeOperations.Length, routeOperations.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task Sanitized_openapi_operation_baseline_is_deterministic_and_current()
    {
        await using var host = await HttpContractHost.StartAsync();
        using var response = await host.Client.GetAsync(
            "/openapi/api.json",
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken));

        var manifest = BuildSanitizedManifest(document.RootElement);
        var baselinePath = Path.Combine(
            FindRepositoryRoot(),
            "Tests",
            "ApiContract.Tests",
            "Baselines",
            "http-openapi-contract-manifest.json");
        if (string.Equals(
                Environment.GetEnvironmentVariable("UPDATE_HTTP_OPENAPI_BASELINE"),
                "1",
                StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            File.WriteAllText(baselinePath, manifest, new UTF8Encoding(false));
            return;
        }

        Assert.True(
            File.Exists(baselinePath),
            $"Missing sanitized OpenAPI baseline: {baselinePath}");
        Assert.Equal(
            File.ReadAllText(baselinePath).Replace("\r\n", "\n", StringComparison.Ordinal),
            manifest);
    }

    private static string BuildSanitizedManifest(JsonElement document)
    {
        var operations = new List<SanitizedOpenApiOperation>();
        foreach (var path in document.GetProperty("paths")
                     .EnumerateObject()
                     .OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            foreach (var operation in path.Value
                         .EnumerateObject()
                         .Where(item => OpenApiMethods.Contains(item.Name))
                         .OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                var value = operation.Value;
                var responses = value.GetProperty("responses");
                var descriptors = responses
                    .EnumerateObject()
                    .OrderBy(item => item.Name, StringComparer.Ordinal)
                    .Select(item =>
                    {
                        var contentTypes = item.Value.TryGetProperty("content", out var content)
                            ? content.EnumerateObject()
                                .Select(media => media.Name)
                                .OrderBy(name => name, StringComparer.Ordinal)
                                .ToArray()
                            : [];
                        return new SanitizedOpenApiResponse(item.Name, contentTypes);
                    })
                    .ToArray();
                if (value.TryGetProperty("deprecated", out var deprecatedElement)
                    && deprecatedElement.ValueKind == JsonValueKind.True)
                {
                    throw new InvalidOperationException(
                        $"Retired operation {operation.Name.ToUpperInvariant()} {path.Name} is still exposed.");
                }
                var canonical = string.Join(
                    "\n",
                    descriptors.Select(item =>
                        $"{item.StatusCode}:{string.Join(',', item.ContentTypes)}"));
                var operationContract =
                    $"{operation.Name.ToUpperInvariant()} {path.Name}\n" +
                    canonical;
                operations.Add(new SanitizedOpenApiOperation(
                    operation.Name.ToUpperInvariant(),
                    path.Name,
                    descriptors,
                    Sha256(operationContract),
                    Sha256(CanonicalJson(responses))));
            }
        }

        var ordered = operations
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .ThenBy(item => item.Method, StringComparer.Ordinal)
            .ToArray();
        var selectedRoles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["POST /api/comments"] = "comment-create",
            ["GET /api/comments/list"] = "comment-list-cursor",
            ["GET /api/feed/user/auth/items"] = "feed-list-cursor",
            ["POST /api/followings/friend/request"] = "friend-request-create",
            ["DELETE /api/followings/friend/request/{id}"] = "friend-request-delete",
            ["GET /api/relations/users/{id}/relation-ids"] = "relationship-ids-cursor",
            ["PUT /api/resources/{type}/{id}/reaction"] = "interaction-reaction",
            ["GET /internal/interaction/authoritative-state"] = "interaction-authority-export",
            ["POST /api/moderation/reports"] = "moderation-report-create",
        };
        var captured = ordered
            .Select(item => new
            {
                Operation = item,
                Key = $"{item.Method} {item.Path}",
            })
            .Where(item => selectedRoles.ContainsKey(item.Key))
            .Select(item => new SanitizedCapturedShape(
                selectedRoles[item.Key],
                item.Operation.Method,
                item.Operation.Path,
                item.Operation.Responses,
                item.Operation.OperationStatusHashSha256,
                item.Operation.ResponseShapeHashSha256))
            .OrderBy(item => item.Role, StringComparer.Ordinal)
            .ToArray();
        if (captured.Length != selectedRoles.Count)
            throw new InvalidOperationException("One or more captured HTTP contract shapes are absent from OpenAPI.");

        var operationStatusHash = Sha256(string.Join(
            "\n---\n",
            ordered.Select(item =>
                $"{item.Method} {item.Path}\n" +
                string.Join("\n", item.Responses.Select(response =>
                    $"{response.StatusCode}:{string.Join(',', response.ContentTypes)}")))));
        var schemaHash = document.TryGetProperty("components", out var components)
            && components.TryGetProperty("schemas", out var schemas)
            ? Sha256(CanonicalJson(schemas))
            : Sha256("{}");
        var baseline = new SanitizedOpenApiBaseline(
            FormatVersion: 2,
            SourceEvidenceCommit: "f969b7363bbac5787625ed0875264abe6a2c415c",
            RegenerationCommand:
                "pwsh -File Tests/ApiContract.Tests/Baselines/regenerate-http-openapi-baseline.ps1",
            OperationCount: ordered.Length,
            OperationStatusHashSha256: operationStatusHash,
            ComponentSchemasHashSha256: schemaHash,
            CapturedShapes: captured,
            Operations: ordered);
        return JsonSerializer.Serialize(baseline, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static string CanonicalJson(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(element, writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject()
                             .OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(element));
        }
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

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

internal sealed record SanitizedOpenApiResponse(
    string StatusCode,
    IReadOnlyList<string> ContentTypes);

internal sealed record SanitizedOpenApiOperation(
    string Method,
    string Path,
    IReadOnlyList<SanitizedOpenApiResponse> Responses,
    string OperationStatusHashSha256,
    string ResponseShapeHashSha256);

internal sealed record SanitizedCapturedShape(
    string Role,
    string Method,
    string Path,
    IReadOnlyList<SanitizedOpenApiResponse> Responses,
    string OperationStatusHashSha256,
    string ResponseShapeHashSha256);

internal sealed record SanitizedOpenApiBaseline(
    int FormatVersion,
    string SourceEvidenceCommit,
    string RegenerationCommand,
    int OperationCount,
    string OperationStatusHashSha256,
    string ComponentSchemasHashSha256,
    IReadOnlyList<SanitizedCapturedShape> CapturedShapes,
    IReadOnlyList<SanitizedOpenApiOperation> Operations);

internal sealed class HttpContractHost : IAsyncDisposable
{
    private const string Scheme = "ApiContract";

    private HttpContractHost(WebApplication app, HttpClient client)
    {
        App = app;
        Client = client;
    }

    public WebApplication App { get; }
    public HttpClient Client { get; }

    public static async Task<HttpContractHost> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(CommentsController).Assembly.GetName().Name,
            EnvironmentName = "Testing",
        });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration["MaxMultipartBodyBytes"] = "1048576";
        var syntheticCursorKey = Convert.ToBase64String(
            Enumerable.Repeat((byte)0x5A, 32).ToArray());
        builder.Configuration["AuthoritativeExport:CursorSigningKey"] = syntheticCursorKey;
        builder.Configuration["DecisionExport:CursorSigningKey"] = syntheticCursorKey;

        builder.Services.AddOpenApi("api");
        builder.Services.AddInspacyProblemDetails();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(CommentsController).Assembly);
        builder.Services
            .AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, ContractAuthenticationHandler>(
                Scheme,
                _ => { });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthPolicies.FromReactApp, policy =>
                policy.RequireAuthenticatedUser());
            options.AddPolicy(AuthPolicies.ClientReadAccess, policy =>
                policy.RequireAuthenticatedUser());
        });
        builder.Services.AddModerationAuthorization();
        builder.Services.AddInteractionAuthorization(builder.Configuration);

        var identity = new ContractIdentityService();
        var commentRepository = new ContractCommentRepository();
        var commentErasureAuthority = new ContractCommentUserErasureAuthority();
        builder.Services.AddSingleton<IIdentityService>(identity);
        builder.Services.AddSingleton<ICommentRepository>(commentRepository);
        builder.Services.AddSingleton(new AddCommentCommand(
            commentRepository,
            identity,
            (CommentMediaIngestionService)null!,
            commentErasureAuthority));
        builder.Services.AddSingleton(new UpdateCommentCommand(
            commentRepository,
            identity,
            (CommentMediaIngestionService)null!,
            commentErasureAuthority));

        var followingRepository = new ContractFollowingRepository();
        builder.Services.AddSingleton<IFollowingRepository>(followingRepository);
        builder.Services.AddSingleton(new AddFriendRequestCommand(followingRepository, identity));
        builder.Services.AddSingleton(new RemoveFriendRequestCommand(followingRepository, identity));
        builder.Services.AddSingleton(new CaseCommands(
            null!,
            null!,
            TimeProvider.System,
            Options.Create(new ModerationSlaOptions())));

        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapCommentsRoutes();
        Feed.API.AppInterfaces.Routes.FeedApiEndpoints.MapFeedRoutes(app);
        app.MapFollowingsRoutes();
        app.MapRelationsRoutes();
        app.MapInteractionRoutes();
        app.MapModerationRoutes();
        app.MapOpenApi();
        await app.StartAsync();

        var client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)) };
        return new HttpContractHost(app, client);
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path)
    {
        var request = Authenticated(new HttpRequestMessage(method, path));
        return Client.SendAsync(request);
    }

    public Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string path, object body)
    {
        var request = Authenticated(new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        });
        return Client.SendAsync(request);
    }

    public Task<HttpResponseMessage> SendMultipartCommentAsync(string path)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent("contract comment"), "Description");
        content.Add(new StringContent("post-42"), "TargetId");
        content.Add(new StringContent("post"), "TargetType");
        var request = Authenticated(new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = content,
        });
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        return Client.SendAsync(request);
    }

    public Task<HttpResponseMessage> SendInvalidMultipartCommentAsync(string path)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent("contract comment"), "Description");
        content.Add(new StringContent("post-42"), "TargetId");
        content.Add(new StringContent("post"), "TargetType");
        var file = new ByteArrayContent([0x01]);
        file.Headers.ContentType = new("application/octet-stream");
        content.Add(file, "files[0].File", "invalid.bin");
        content.Add(new StringContent("1"), "files[0].MediaType");
        var request = Authenticated(new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = content,
        });
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        return Client.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
    }

    private static HttpRequestMessage Authenticated(HttpRequestMessage request)
    {
        request.Headers.Add(ContractAuthenticationHandler.UserHeader, "7");
        return request;
    }
}

internal sealed class ContractAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string UserHeader = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out StringValues values)
            || !int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || id <= 0)
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim>
        {
            new("sub", id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.NameIdentifier, id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Role, "ModerationReporter"),
            new(ClaimTypes.Role, "ModerationTriage"),
            new(ClaimTypes.Role, "ModerationInvestigator"),
            new(ClaimTypes.Role, "ModerationDecision"),
            new(ClaimTypes.Role, "ModerationAdmin"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, Scheme.Name)));
    }
}

internal sealed class ContractIdentityService : IIdentityService
{
    public AuthUser User { get; } = new(7, "Contract User");
}

internal sealed class ContractCommentRepository : ICommentRepository
{
    private readonly Dictionary<int, CommentPost> _comments = [];
    private int _nextId = 500;

    public Task<CommentPost> AddAsync(CommentPost comment, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetEntityId(comment, ++_nextId);
        _comments.Add(comment.Id, comment);
        return Task.FromResult(comment);
    }

    public Task<CommentPost?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(_comments.GetValueOrDefault(id));

    public Task RemoveAsync(CommentPost comment, CancellationToken cancellationToken)
    {
        _comments.Remove(comment.Id);
        return Task.CompletedTask;
    }

    public Task<CommentPost> SaveUpdatedAsync(CommentPost comment, CancellationToken cancellationToken) =>
        Task.FromResult(comment);

    private static void SetEntityId(BaseEntity entity, int id)
    {
        var idField = typeof(BaseEntity).GetField(
            "_id",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The entity id field could not be found.");
        idField.SetValue(entity, id);
    }
}

internal sealed class ContractCommentUserErasureAuthority : ICommentUserErasureAuthority
{
    public Task<ICommentUserMutationScope> AcquireMutationAsync(
        IReadOnlyCollection<int> userIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<ICommentUserMutationScope>(new MutationScope());

    public Task<bool> IsErasedAsync(int userId, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<IReadOnlySet<int>> GetErasedAsync(
        IReadOnlyCollection<int> userIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

    private sealed class MutationScope : ICommentUserMutationScope
    {
        public bool Allowed => true;

        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class ContractFollowingRepository : IFollowingRepository
{
    private readonly List<FriendRequest> _requests = [];
    private readonly Dictionary<int, UserRelationAnalytics> _analytics = [];
    private int _nextId = 800;

    public Task<IRelationshipPairMutation> AcquireRelationshipPairMutation(
        int firstUserId, int secondUserId, CancellationToken cancellationToken) =>
        Task.FromResult<IRelationshipPairMutation>(NoopRelationshipPairMutation.Instance);

    public Task<IUserMutationScope> AcquireUserMutation(int userId, CancellationToken cancellationToken) =>
        Task.FromResult<IUserMutationScope>(NoopUserMutationScope.Instance);

    public Task<IUserMutationScope> AcquireUserErasureMutation(int userId, CancellationToken cancellationToken) =>
        Task.FromResult<IUserMutationScope>(NoopUserMutationScope.Instance);

    public Task EnsureUsersNotErased(
        IReadOnlyCollection<int> userIds,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<UserBlock?> GetBlock(
        int blockerUserId, int blockedUserId, CancellationToken cancellationToken) =>
        Task.FromResult<UserBlock?>(null);

    public Task<bool> IsBlockedEitherDirection(
        int firstUserId, int secondUserId, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<CategoryFollow?> GetCategoryFollow(
        int userId, int? categoryId, string? categoryName, CancellationToken cancellationToken) =>
        Task.FromResult<CategoryFollow?>(null);

    public Task<Follow?> GetFollow(int userId, int followedId, CancellationToken cancellationToken) =>
        Task.FromResult<Follow?>(null);

    public Task<FriendRequest?> GetFriendRequest(
        int userId,
        int friendId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_requests.SingleOrDefault(request =>
            (request.SenderUserId == userId && request.RecipientUserId == friendId)
            || (request.SenderUserId == friendId && request.RecipientUserId == userId)));

    public Task<FriendRequest?> GetFriendRequestForApproval(
        int requestId, int friendId, CancellationToken cancellationToken) =>
        Task.FromResult<FriendRequest?>(null);

    public Task<bool> IsFriend(int userId, int friendId, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<IReadOnlyList<Friendship>> GetFriendships(
        int userId, int friendId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Friendship>>([]);

    public Task<FriendshipSubscriptionFeature?> GetSubscriptionFeature(
        string subscriptionId,
        int requesterId,
        int ownerId,
        CancellationToken cancellationToken) =>
        Task.FromResult<FriendshipSubscriptionFeature?>(null);

    public Task<UserRelationAnalytics> GetOrCreateAnalytics(
        int userId,
        int shardDiscriminator,
        CancellationToken cancellationToken)
    {
        if (!_analytics.TryGetValue(userId, out var value))
        {
            value = new UserRelationAnalytics(userId, shardDiscriminator);
            _analytics.Add(userId, value);
        }
        return Task.FromResult(value);
    }

    public Task Add<T>(T entity, CancellationToken cancellationToken) where T : class
    {
        if (entity is FriendRequest request)
        {
            SetEntityId(request, ++_nextId);
            _requests.Add(request);
        }
        return Task.CompletedTask;
    }

    public Task AddRange<T>(IEnumerable<T> entities, CancellationToken cancellationToken)
        where T : class => Task.CompletedTask;

    public void Remove<T>(T entity) where T : class
    {
        if (entity is FriendRequest request)
            _requests.Remove(request);
    }

    public void RemoveRange<T>(IEnumerable<T> entities) where T : class
    {
    }

    public Task SaveChanges(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void SetEntityId(BaseEntity entity, int id) =>
        typeof(BaseEntity)
            .GetField("_id", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(entity, id);
}
