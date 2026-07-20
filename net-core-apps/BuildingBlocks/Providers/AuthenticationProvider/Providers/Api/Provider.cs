using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AuthenticationProvider.Abstractions;
using AuthenticationProvider.Dtos;

namespace AuthenticationProvider.Providers.Api
{
    public class AuthenticationApiProvider : AuthenticationApiBase, IAuthApi
    {
        private readonly AccessTokenCache _tokenCache;

        public AuthenticationApiProvider(HttpClient httpClient, AccessTokenCache tokenCache) : base(httpClient)
        {
            _tokenCache = tokenCache;
        }

        public async Task<string> GetAccessToken(
            string clientId,
            string clientSecret,
            string[] scopes,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
            ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);
            ArgumentNullException.ThrowIfNull(scopes);

            return await _tokenCache.GetOrCreateAsync(
                clientId,
                clientSecret,
                scopes,
                async token =>
                {
                    var response = await GetTokenAsync(clientId, clientSecret, scopes, token);
                    if (response is null || string.IsNullOrWhiteSpace(response.access_token) || response.expires_in <= 0)
                        throw new InvalidOperationException("The identity service returned an invalid token response.");

                    return new TokenResponse
                    {
                        AccessToken = response.access_token,
                        ExpiresAt = DateTime.UtcNow.AddSeconds(response.expires_in)
                    };
                },
                cancellationToken);
        }

        private async Task<AuthenticationResponseDto> GetTokenAsync(
            string clientId,
            string clientSecret,
            string[] scopes,
            CancellationToken cancellationToken)
        {
            try
            {
                var requestContent = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>{
                    new("client_id", clientId),
                    new("client_secret", clientSecret),
                    new("grant_type", "client_credentials"),
                    new ("scope", string.Join(" ", scopes))
                });

                using var request = new HttpRequestMessage(HttpMethod.Post, "connect/token")
                {
                    Content = requestContent
                };

                var response = await _httpClient.SendAsync(request, cancellationToken);

                return await getResponseAsync<AuthenticationResponseDto>(
                    response,
                    cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                handleError(ex);
                throw;
            }
        }
    }

}
