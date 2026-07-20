using Feed.Application.Features.Audience;
using AuthenticationProvider.Abstractions;
using MessageBrokerContracts.Following;
using Microsoft.Extensions.Options;
using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Infra.Providers
{
    /// <summary>Bounded authority adapter used only by the background bootstrap job.</summary>
    public sealed class FollowingSnapshotClientOptions
    {
        public const string SectionName = "Following:Snapshot";

        public string ServiceUrl { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string ReadScope { get; set; } = string.Empty;
        public int RequestTimeoutSeconds { get; set; } = 15;

        public static bool IsValid(FollowingSnapshotClientOptions options) =>
            options is not null
            && Uri.TryCreate(options.ServiceUrl, UriKind.Absolute, out _)
            && !string.IsNullOrWhiteSpace(options.ClientId)
            && !string.IsNullOrWhiteSpace(options.ClientSecret)
            && !string.IsNullOrWhiteSpace(options.ReadScope)
            && options.RequestTimeoutSeconds is >= 1 and <= 30;
    }

    public sealed class FollowingRelationshipBootstrapSource(
        HttpClient httpClient,
        IAuthService authService,
        IOptions<FollowingSnapshotClientOptions> options)
        : IRelationshipBootstrapSource
    {
        private readonly FollowingSnapshotClientOptions _options = options.Value;

        public async Task<RelationshipSnapshotPage> ReadPageAsync(
            long afterCursor,
            int pageSize,
            long? snapshotHighWatermark,
            CancellationToken cancellationToken)
        {
            if (afterCursor < 0)
                throw new ArgumentOutOfRangeException(nameof(afterCursor));
            if (pageSize is < 1 or > 500)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            if (snapshotHighWatermark is < 0)
                throw new ArgumentOutOfRangeException(nameof(snapshotHighWatermark));

            var token = await authService.Api.GetAccessToken(
                _options.ClientId,
                _options.ClientSecret,
                [_options.ReadScope],
                cancellationToken);
            var endpoint = string.Concat(
                "api/internal/relationships/snapshot?afterCursor=",
                afterCursor.ToString(CultureInfo.InvariantCulture),
                "&pageSize=",
                pageSize.ToString(CultureInfo.InvariantCulture),
                snapshotHighWatermark.HasValue
                    ? "&snapshotHighWatermark="
                        + snapshotHighWatermark.Value.ToString(CultureInfo.InvariantCulture)
                    : string.Empty);
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<RelationshipSnapshotPage>(
                cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Following returned an empty snapshot page.");
        }
    }
}
