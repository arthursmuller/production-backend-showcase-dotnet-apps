using AuthUtils;
using Following.API.AppInterfaces.Routes;
using Following.Application.Dtos;
using Following.Application.Features.Followings;
using Following.Domain.AggregatesModel;
using Following.Domain.Exceptions;
using Following.Domain.Repositories;
using IdentityProvider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using Xunit;

namespace Following.UnitTests;

public sealed class FollowingCursorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Cursor_round_trips_a_stable_position_within_the_length_bound()
    {
        var (codec, _) = CreateCodec();
        var fingerprint = FollowingCursorFingerprint.Create(
            "friends", "user:42", "42");
        var position = new FollowingCursorPosition(Now.UtcDateTime, 123);

        var encoded = codec.Encode(position, fingerprint);

        Assert.True(encoded.Length <= FollowingCursorCodec.MaximumEncodedLength);
        Assert.DoesNotContain('=', encoded);
        Assert.True(codec.TryDecode(encoded, fingerprint, out var decoded));
        Assert.Equal(position, decoded);
    }

    [Fact]
    public void Cursor_rejects_tampering_and_a_different_actor_or_filter_scope()
    {
        var (codec, _) = CreateCodec();
        var original = FollowingCursorFingerprint.Create(
            "friend_requests", "user:42", "received", "0");
        var encoded = codec.Encode(
            new FollowingCursorPosition(Now.UtcDateTime, 77),
            original);
        var replacement = encoded[^1] == 'A' ? 'B' : 'A';
        var tampered = encoded[..^1] + replacement;

        Assert.False(codec.TryDecode(tampered, original, out _));
        Assert.False(codec.TryDecode(
            encoded,
            FollowingCursorFingerprint.Create(
                "friend_requests", "user:43", "received", "0"),
            out _));
        Assert.False(codec.TryDecode(
            encoded,
            FollowingCursorFingerprint.Create(
                "friend_requests", "user:42", "sent", "0"),
            out _));
    }

    [Fact]
    public void Cursor_expires_after_the_configured_lifetime()
    {
        var (codec, clock) = CreateCodec(lifetimeSeconds: 600);
        var fingerprint = FollowingCursorFingerprint.Create(
            "category_follows", "user:42", "42");
        var encoded = codec.Encode(
            new FollowingCursorPosition(Now.UtcDateTime, 9),
            fingerprint);

        clock.Advance(TimeSpan.FromSeconds(601));

        Assert.False(codec.TryDecode(encoded, fingerprint, out _));
    }

    [Fact]
    public void Cursor_options_require_a_base64_key_of_at_least_32_bytes_and_bounded_lifetime()
    {
        Assert.False(FollowingCursorOptions.IsValid(new FollowingCursorOptions
        {
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
            LifetimeSeconds = 600,
        }));
        Assert.False(FollowingCursorOptions.IsValid(new FollowingCursorOptions
        {
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            LifetimeSeconds = 299,
        }));
        Assert.True(FollowingCursorOptions.IsValid(new FollowingCursorOptions
        {
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            LifetimeSeconds = 600,
        }));
    }

    [Fact]
    public async Task Friend_request_cursor_is_bound_to_authenticated_actor_and_filter()
    {
        var (codec, _) = CreateCodec();
        var firstRepository = new CapturingReadRepository
        {
            FriendRequestSlice = new FollowingCursorSlice<FriendRequestCursorReadModel>(
                [new FriendRequestCursorReadModel(
                    10, 7, "sender", 42, "recipient",
                    FriendRequestStatus.Pending, Now.UtcDateTime)],
                new FollowingCursorPosition(Now.UtcDateTime, 10),
                true),
        };
        var firstQuery = new ListFriendRequestsCursorQuery(
            firstRepository,
            new StubIdentity(42),
            codec);
        var first = await firstQuery.Handle(
            new FriendRequestsCursorCriteria
            {
                PageSize = 1,
                SentRequests = false,
                Status = FriendRequestStatus.Pending,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(42, firstRepository.FriendRequestActorId);
        Assert.NotNull(first.NextCursor);

        var otherActorQuery = new ListFriendRequestsCursorQuery(
            new CapturingReadRepository(),
            new StubIdentity(43),
            codec);
        await Assert.ThrowsAsync<FollowingException>(() => otherActorQuery.Handle(
            new FriendRequestsCursorCriteria
            {
                PageSize = 1,
                Cursor = first.NextCursor,
                SentRequests = false,
                Status = FriendRequestStatus.Pending,
            },
            TestContext.Current.CancellationToken));

        var changedFilterQuery = new ListFriendRequestsCursorQuery(
            new CapturingReadRepository(),
            new StubIdentity(42),
            codec);
        await Assert.ThrowsAsync<FollowingException>(() => changedFilterQuery.Handle(
            new FriendRequestsCursorCriteria
            {
                PageSize = 1,
                Cursor = first.NextCursor,
                SentRequests = true,
                Status = FriendRequestStatus.Pending,
            },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Friends_without_an_explicit_owner_use_the_authenticated_actor()
    {
        var (codec, _) = CreateCodec();
        var repository = new CapturingReadRepository();
        var query = new ListFriendsCursorQuery(
            repository,
            new StubIdentity(42),
            codec);

        await query.Handle(
            new FriendsCursorCriteria { PageSize = 25 },
            TestContext.Current.CancellationToken);

        Assert.Equal(42, repository.FriendsOwnerId);
    }

    [Fact]
    public async Task Cursor_surface_rejects_oversized_pages_and_cursors_before_repository_access()
    {
        var (codec, _) = CreateCodec();
        var repository = new CapturingReadRepository();
        var query = new ListFriendsCursorQuery(
            repository,
            new StubIdentity(42),
            codec);

        await Assert.ThrowsAsync<FollowingException>(() => query.Handle(
            new FriendsCursorCriteria { PageSize = 101 },
            TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<FollowingException>(() => query.Handle(
            new FriendsCursorCriteria
            {
                PageSize = 25,
                Cursor = new string('A', FollowingCursorCodec.MaximumEncodedLength + 1),
            },
            TestContext.Current.CancellationToken));
        Assert.Null(repository.FriendsOwnerId);
    }

    [Fact]
    public async Task Cancellation_is_propagated_to_the_repository_query()
    {
        var (codec, _) = CreateCodec();
        var query = new ListFriendsCursorQuery(
            new CapturingReadRepository(),
            new StubIdentity(42),
            codec);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.Handle(
            new FriendsCursorCriteria { PageSize = 25 },
            cancellation.Token));
    }

    [Fact]
    public async Task Block_list_is_bound_to_the_authenticated_user_and_cursor_scope()
    {
        var (codec, _) = CreateCodec();
        var repository = new CapturingReadRepository();
        var query = new ListUserBlocksCursorQuery(
            repository,
            new StubIdentity(42),
            codec);

        var page = await query.Handle(
            new UserBlocksCursorCriteria { PageSize = 25 },
            TestContext.Current.CancellationToken);

        Assert.Equal(42, repository.BlockActorId);
        Assert.Empty(page.Items);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Routes_preserve_user_and_administrative_authorization_boundaries()
    {
        using var services = new ServiceCollection()
            .AddRouting()
            .AddAuthorization()
            .BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        routes.MapFollowingsRoutes();
        routes.MapRelationsRoutes();
        var endpoints = routes.DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .GroupBy(endpoint => endpoint.RoutePattern.RawText!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var path in new[]
                 {
                     "/api/followings/friend/request/list",
                     "/api/followings/friend/list",
                     "/api/followings/user/follow/list",
                     "/api/followings/category/follow/list",
                     "/api/followings/blocks/{id:int}",
                     "/api/followings/blocks",
                 })
        {
            var authorization = endpoints[path]
                .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            Assert.Contains(authorization, item => item.Policy == AuthPolicies.FromReactApp);
        }

        var administrative = endpoints[
            "/api/relations/users/{id:int}/relation-ids"].Single()
            .Metadata.GetOrderedMetadata<IAuthorizeData>();
        Assert.Contains(
            administrative,
            item => item.Policy == AuthPolicies.ClientReadAccess);
        var snapshotAuthorization = endpoints[
            "/api/internal/relationships/snapshot"].Single()
            .Metadata.GetOrderedMetadata<IAuthorizeData>();
        Assert.Contains(
            snapshotAuthorization,
            item => item.Policy == AuthPolicies.ClientReadAccess);
        await Task.CompletedTask;
    }

    [Fact]
    public void Administrative_cursor_scope_uses_a_stable_authenticated_client_claim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("client_id", "feed-projection"),
                new Claim("iss", "https://identity.example.test"),
            ],
            "test"));

        var scope = RelationsApiEndpoints.GetAuthenticatedClientScope(principal);

        Assert.Equal(
            "client_id:feed-projection|iss:https://identity.example.test",
            scope);
        var exception = Assert.Throws<FollowingException>(() =>
            RelationsApiEndpoints.GetAuthenticatedClientScope(
                new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test"))));
        Assert.Equal(403, exception.StatusCode);
    }

    [Fact]
    public void Internal_snapshot_requires_a_non_react_service_principal()
    {
        Assert.True(RelationsApiEndpoints.IsServicePrincipal(
            Principal(new Claim("client_id", "feed-projection"))));
        Assert.True(RelationsApiEndpoints.IsServicePrincipal(
            Principal(new Claim("azp", "comment-projection"))));
        Assert.False(RelationsApiEndpoints.IsServicePrincipal(
            Principal(new Claim("client_id", "react"))));
        Assert.False(RelationsApiEndpoints.IsServicePrincipal(
            Principal(new Claim("sub", "regular-user"))));

        static ClaimsPrincipal Principal(Claim claim) =>
            new(new ClaimsIdentity([claim], "test"));
    }

    private static (FollowingCursorCodec Codec, ManualTimeProvider Clock) CreateCodec(
        int lifetimeSeconds = 3_600)
    {
        var clock = new ManualTimeProvider(Now);
        var codec = new FollowingCursorCodec(
            Options.Create(new FollowingCursorOptions
            {
                SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                LifetimeSeconds = lifetimeSeconds,
            }),
            clock);
        return (codec, clock);
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }

    private sealed class StubIdentity(int userId) : IIdentityService
    {
        public AuthUser User { get; } = new(userId, $"user-{userId}");
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider)
        : IEndpointRouteBuilder
    {
        public IApplicationBuilder CreateApplicationBuilder() =>
            new ApplicationBuilder(ServiceProvider);

        public ICollection<EndpointDataSource> DataSources { get; } =
            new List<EndpointDataSource>();

        public IServiceProvider ServiceProvider { get; } = serviceProvider;
    }

    private sealed class CapturingReadRepository : IFollowingReadRepository
    {
        public int? FriendRequestActorId { get; private set; }
        public int? FriendsOwnerId { get; private set; }
        public int? BlockActorId { get; private set; }
        public FollowingCursorSlice<FriendRequestCursorReadModel> FriendRequestSlice { get; init; } =
            Empty<FriendRequestCursorReadModel>();

        public Task<FollowingCursorSlice<FriendRequestCursorReadModel>> ListFriendRequestsCursor(
            int currentUserId, bool sentRequests, FriendRequestStatus status,
            FollowingCursorWindow window, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FriendRequestActorId = currentUserId;
            return Task.FromResult(FriendRequestSlice);
        }

        public Task<FollowingCursorSlice<FriendshipCursorReadModel>> ListFriendsCursor(
            int ownerUserId, FollowingCursorWindow window, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FriendsOwnerId = ownerUserId;
            return Task.FromResult(Empty<FriendshipCursorReadModel>());
        }

        public Task<FollowingCursorSlice<FollowCursorReadModel>> ListFollowsCursor(
            int userId, bool following, FollowingCursorWindow window,
            CancellationToken cancellationToken) =>
            Task.FromResult(Empty<FollowCursorReadModel>());

        public Task<FollowingCursorSlice<CategoryFollowCursorReadModel>> ListCategoryFollowsCursor(
            int userId, FollowingCursorWindow window, CancellationToken cancellationToken) =>
            Task.FromResult(Empty<CategoryFollowCursorReadModel>());

        public Task<FollowingCursorSlice<UserBlockCursorReadModel>> ListUserBlocksCursor(
            int blockerUserId, FollowingCursorWindow window,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BlockActorId = blockerUserId;
            return Task.FromResult(Empty<UserBlockCursorReadModel>());
        }

        public Task<FollowingCursorSlice<RelationIdCursorReadModel>> ListRelationIdsCursor(
            int userId, RelationIdKind kind, FollowingCursorWindow window,
            CancellationToken cancellationToken) =>
            Task.FromResult(Empty<RelationIdCursorReadModel>());

        private static FollowingCursorSlice<T> Empty<T>() where T : IFollowingCursorReadModel =>
            new(Array.Empty<T>(), null!, false);

        public Task<IReadOnlyList<CategoryFollow>> FindCategoryFollows(
            int userId, IReadOnlyCollection<int> categoryIds,
            IReadOnlyCollection<string> categoryNames, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyCollection<int>> FindFollowedUserIds(
            int userId, IReadOnlyCollection<int> followedUserIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RelationsSnapshot> GetRelations(
            int userId, int otherUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<UserRelationAnalytics> GetUserAnalytics(
            int userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
