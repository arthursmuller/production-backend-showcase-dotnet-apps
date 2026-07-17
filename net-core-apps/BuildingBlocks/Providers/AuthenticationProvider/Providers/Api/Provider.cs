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
        private TokenResponse _clientCredentialAccessToken;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public AuthenticationApiProvider(HttpClient httpClient) : base(httpClient) { }

        public async Task<string> GetAccessToken(string clientId, string clientSecret, string[] scopes)
        {
            if (_clientCredentialAccessToken != null && _clientCredentialAccessToken.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
            {
                return _clientCredentialAccessToken.AccessToken;
            }

            await _semaphore.WaitAsync();
            try
            {
                if (_clientCredentialAccessToken != null && _clientCredentialAccessToken.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
                {
                    return _clientCredentialAccessToken.AccessToken;
                }

                var tokenResponse = await getToken(clientId, clientSecret, scopes);

                if (tokenResponse == null)
                {
                    throw new InvalidOperationException("Failed to acquire access token");
                }

                _clientCredentialAccessToken = new TokenResponse
                {
                    AccessToken = tokenResponse.access_token,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.expires_in)
                };

                return _clientCredentialAccessToken.AccessToken;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task<AuthenticationResponseDto> getToken(string clientId, string clientSecret, string[] scopes)
        {
            AuthenticationResponseDto res = null;

            try
            {
                var requestContent = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>{
                    new("client_id", clientId),
                    new("client_secret", clientSecret),
                    new("grant_type", "client_credentials"),
                    new ("scope", string.Join(" ", scopes)) 
                });

                var request = new HttpRequestMessage(HttpMethod.Post, "connect/token")
                {
                    Content = requestContent
                };

                var response = await _httpClient.SendAsync(request);

                
                res = await getResponseAsync<AuthenticationResponseDto>(response);
            }
            catch (HttpRequestException ex)
            {
                handleError(ex);
            }

            return res;
        }
    }

}
