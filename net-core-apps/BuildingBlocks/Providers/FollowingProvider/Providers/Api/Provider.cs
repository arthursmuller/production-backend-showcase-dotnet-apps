using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using AuthenticationProvider.Abstractions;
using FollowingProvider.Abstractions;
using MessageBrokerContracts.Following;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace FollowingProvider.Providers.Api
{
    public class FollowingApiProvider : FollowingApiBase, IFollowingApi
    {
        private const long MaximumSnapshotResponseBytes = 512 * 1024;
        private readonly IAuthService _authService;
        private readonly ClientCredentialTokenConfig _config;

        public FollowingApiProvider(HttpClient httpClient, ClientCredentialTokenConfig config, IAuthService authProvider) : base(httpClient)
        {
            _authService = authProvider ?? throw new ArgumentNullException(nameof(authProvider));
            _config = ValidateConfig(config);
        }
        public async Task<RelationshipSnapshotPage> GetRelationshipSnapshotPageAsync(
            long afterCursor,
            int pageSize,
            long? snapshotHighWatermark,
            System.Threading.CancellationToken cancellationToken)
        {
            if (afterCursor < 0)
                throw new ArgumentOutOfRangeException(nameof(afterCursor));
            if (pageSize is < 1 or > 500)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            if (snapshotHighWatermark is < 0)
                throw new ArgumentOutOfRangeException(nameof(snapshotHighWatermark));
            if (afterCursor > 0 && !snapshotHighWatermark.HasValue)
            {
                throw new ArgumentException(
                    "A continuation page requires a snapshot high watermark.",
                    nameof(snapshotHighWatermark));
            }

            try
            {
                var token = await GetAccessTokenAsync(cancellationToken);
                var endpoint = string.Concat(
                    "api/internal/relationships/snapshot?afterCursor=",
                    afterCursor.ToString(CultureInfo.InvariantCulture),
                    "&pageSize=",
                    pageSize.ToString(CultureInfo.InvariantCulture),
                    snapshotHighWatermark.HasValue
                        ? "&snapshotHighWatermark=" +
                            snapshotHighWatermark.Value.ToString(CultureInfo.InvariantCulture)
                        : string.Empty);
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                var page = await ReadSnapshotResponseAsync(response, cancellationToken);
                ValidateSnapshotPage(page, afterCursor, pageSize, snapshotHighWatermark);
                return page;
            }
            catch (HttpRequestException exception)
            {
                HandleError(exception);
                throw;
            }
            catch (JsonException)
            {
                throw new FollowingProviderException(
                    "The following service returned an invalid relationship snapshot.");
            }
        }

        private static async Task<RelationshipSnapshotPage> ReadSnapshotResponseAsync(
            HttpResponseMessage response,
            System.Threading.CancellationToken cancellationToken)
        {
            using (response)
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaximumSnapshotResponseBytes)
                {
                    throw new HttpRequestException(
                        "The following service snapshot exceeded its response budget.",
                        inner: null,
                        response.StatusCode);
                }

                await response.Content.LoadIntoBufferAsync(
                    MaximumSnapshotResponseBytes,
                    cancellationToken);
                return await JsonSerializer.DeserializeAsync<RelationshipSnapshotPage>(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                    cancellationToken)
                    ?? throw new JsonException(
                        "The following service returned JSON null instead of a relationship snapshot.");
            }
        }

        private static void ValidateSnapshotPage(
            RelationshipSnapshotPage page,
            long afterCursor,
            int requestedPageSize,
            long? requestedHighWatermark)
        {
            if (page is null
                || page.Items is null
                || page.Items.Count > requestedPageSize
                || page.SnapshotHighWatermark < 0
                || (requestedHighWatermark.HasValue
                    && page.SnapshotHighWatermark != requestedHighWatermark.Value)
                || page.NextCursor < afterCursor
                || page.NextCursor > page.SnapshotHighWatermark
                || (page.HasMore && (page.Items.Count == 0 || page.NextCursor == afterCursor))
                || (!page.HasMore && page.NextCursor !=
                    (page.Items.Count == 0 ? afterCursor : page.Items[^1].Cursor))
                || page.SnapshotAsOfUtc.Kind != DateTimeKind.Utc
                || page.Items.Select(item => item.Cursor).Distinct().Count() != page.Items.Count)
            {
                throw new FollowingProviderException(
                    "The following service returned an invalid relationship snapshot.");
            }

            long previousCursor = afterCursor;
            foreach (var item in page.Items)
            {
                if (item is null
                    || item.Cursor <= previousCursor
                    || item.Cursor > page.SnapshotHighWatermark
                    || item.StreamId == Guid.Empty
                    || item.LastEventId == Guid.Empty
                    || item.StreamVersion <= 0
                    || item.OwnerUserId <= 0
                    || item.ViewerUserId <= 0
                    || item.OwnerUserId == item.ViewerUserId
                    || !RelationshipKinds.IsSupported(item.RelationshipKind)
                    || !RelationshipStates.IsSupported(item.State)
                    || item.AsOfUtc.Kind != DateTimeKind.Utc)
                {
                    throw new FollowingProviderException(
                        "The following service returned an invalid relationship snapshot.");
                }

                previousCursor = item.Cursor;
            }

            if (page.NextCursor != previousCursor)
            {
                throw new FollowingProviderException(
                    "The following service returned an invalid relationship snapshot.");
            }
        }

        private Task<string> GetAccessTokenAsync(System.Threading.CancellationToken cancellationToken) =>
            _authService.Api.GetAccessToken(
                _config.ClientId,
                _config.ClientSecret,
                new[] { _config.ReadScope },
                cancellationToken);

        private static ClientCredentialTokenConfig ValidateConfig(
            ClientCredentialTokenConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            if (string.IsNullOrWhiteSpace(config.ClientId)
                || config.ClientId.Length > 256
                || string.IsNullOrWhiteSpace(config.ClientSecret)
                || config.ClientSecret.Length > 4_096
                || string.IsNullOrWhiteSpace(config.ReadScope)
                || config.ReadScope.Length > 256)
            {
                throw new InvalidOperationException(
                    "Following provider client-credential configuration is invalid.");
            }

            return config;
        }
    }
}
