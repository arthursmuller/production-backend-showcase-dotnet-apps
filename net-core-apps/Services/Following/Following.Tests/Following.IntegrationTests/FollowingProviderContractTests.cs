using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AuthenticationProvider.Abstractions;
using AuthenticationProvider.Providers.Api;
using FollowingProvider;
using FollowingProvider.Providers.Api;
using MessageBrokerContracts.Following;
using Xunit;

namespace Following.UnitTests;

public sealed class FollowingProviderContractTests
{
    [Fact]
    public async Task Snapshot_provider_uses_client_credentials_and_preserves_fixed_boundary()
    {
        var page = ValidPage(highWatermark: 11);
        var handler = new RecordingHandler(_ => JsonResponse(page));
        var auth = new RecordingAuthService();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://following.example.test/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var provider = new FollowingApiProvider(
            client,
            new ClientCredentialTokenConfig(
                "feed-projection",
                "test-client-secret",
                "following_api:read"),
            auth);

        var result = await provider.GetRelationshipSnapshotPageAsync(
            afterCursor: 5,
            pageSize: 10,
            snapshotHighWatermark: 11,
            TestContext.Current.CancellationToken);

        Assert.Equal(11, result.SnapshotHighWatermark);
        Assert.Equal(
            "/api/internal/relationships/snapshot?afterCursor=5&pageSize=10&snapshotHighWatermark=11",
            handler.RequestUri!.PathAndQuery);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "test-access-token"), handler.Authorization);
        Assert.Equal("feed-projection", auth.ApiImpl.ClientId);
        Assert.Equal("following_api:read", Assert.Single(auth.ApiImpl.Scopes!));
    }

    [Fact]
    public async Task Snapshot_provider_fails_closed_on_malformed_or_boundary_switching_payloads()
    {
        var invalid = ValidPage(highWatermark: 12);
        var handler = new RecordingHandler(_ => JsonResponse(invalid));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://following.example.test/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var provider = CreateProvider(client);

        var exception = await Assert.ThrowsAsync<FollowingProviderException>(() =>
            provider.GetRelationshipSnapshotPageAsync(
                afterCursor: 5,
                pageSize: 10,
                snapshotHighWatermark: 11,
                TestContext.Current.CancellationToken));

        Assert.DoesNotContain("12", exception.Message, StringComparison.Ordinal);
        Assert.Contains("invalid relationship snapshot", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_propagates_cancellation_and_never_copies_response_bodies_to_errors()
    {
        const string sensitiveBody = "sensitive-upstream-payload";
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent(sensitiveBody, Encoding.UTF8, "application/json"),
        });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://following.example.test/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var provider = CreateProvider(client);

        var failure = await Assert.ThrowsAsync<FollowingProviderException>(() =>
            provider.GetRelationshipSnapshotPageAsync(
                0,
                10,
                null,
                TestContext.Current.CancellationToken));
        Assert.DoesNotContain(sensitiveBody, failure.ToString(), StringComparison.Ordinal);

        using var cancellation = new CancellationTokenSource();
        var blocking = new RecordingHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("unreachable");
        });
        using var blockingClient = new HttpClient(blocking)
        {
            BaseAddress = new Uri("https://following.example.test/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var blockingProvider = CreateProvider(blockingClient);
        var request = blockingProvider.GetRelationshipSnapshotPageAsync(
            0,
            10,
            null,
            cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    [Fact]
    public async Task Snapshot_provider_rejects_an_oversized_response_before_deserialization()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                new string('x', 600 * 1024),
                Encoding.UTF8,
                "application/json"),
        });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://following.example.test/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };

        var exception = await Assert.ThrowsAsync<FollowingProviderException>(() =>
            CreateProvider(client).GetRelationshipSnapshotPageAsync(
                0,
                500,
                null,
                TestContext.Current.CancellationToken));

        Assert.Equal("The following service request failed.", exception.Message);
    }

    private static FollowingApiProvider CreateProvider(HttpClient client) =>
        new(
            client,
            new ClientCredentialTokenConfig(
                "feed-projection",
                "test-client-secret",
                "following_api:read"),
            new RecordingAuthService());

    private static RelationshipSnapshotPage ValidPage(long highWatermark) => new()
    {
        Items =
        [
            new RelationshipStateSnapshot
            {
                Cursor = 11,
                StreamId = Guid.Parse("a28dfe71-9046-4a3b-9c6e-403b5d920e89"),
                LastEventId = Guid.Parse("0bb310e8-cce6-43d4-871e-174a61647e32"),
                StreamVersion = 3,
                OwnerUserId = 10,
                ViewerUserId = 20,
                RelationshipKind = RelationshipKinds.Friend,
                State = RelationshipStates.Active,
                AsOfUtc = new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc),
            },
        ],
        NextCursor = 11,
        HasMore = false,
        SnapshotHighWatermark = highWatermark,
        SnapshotAsOfUtc = new DateTime(2026, 7, 17, 11, 0, 0, DateTimeKind.Utc),
    };

    private static HttpResponseMessage JsonResponse<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(value),
            Encoding.UTF8,
            "application/json"),
    };

    private sealed class RecordingAuthService : IAuthService
    {
        public RecordingAuthApi ApiImpl { get; } = new();
        public IAuthApi Api => ApiImpl;
    }

    private sealed class RecordingAuthApi : IAuthApi
    {
        public string? ClientId { get; private set; }
        public string[]? Scopes { get; private set; }

        public Task<string> GetAccessToken(
            string clientId,
            string clientSecret,
            string[] scopes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClientId = clientId;
            Scopes = scopes;
            return Task.FromResult("test-access-token");
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((request, _) => Task.FromResult(respond(request)))
        {
        }

        public RecordingHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
            _respond = respond;

        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            return _respond(request, cancellationToken);
        }
    }
}
